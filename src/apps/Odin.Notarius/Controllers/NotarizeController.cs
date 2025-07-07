// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Notary;
using Odin.Core.Storage.Database.Notary.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.KeyChain;

namespace Odin.Notarius.Controllers;

[ApiController]
[Route("[controller]")]
public class NotarizeController : ControllerBase
{
    private readonly ILogger<NotarizeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotaryDatabase _db;
    private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private readonly bool _simulate = true;
    public static UnixTimeUtc simulateTime = 0;
    private ConcurrentDictionary<string, PendingRegistrationData> _pendingRegistrationCache;
    private static OdinId _notaryIdentity;
    private SensitiveByteArray _pwd;
    private EccFullKeyData _ecc;

    public NotarizeController(ILogger<NotarizeController> logger, IHttpClientFactory httpClientFactory, NotaryDatabase db, ConcurrentDictionary<string, PendingRegistrationData> preregisteredCache, SensitiveByteArray pwd, EccFullKeyData ecc)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _pendingRegistrationCache = preregisteredCache;
        _notaryIdentity = new OdinId("notarius.odin.earth");
        _pwd = pwd;
        _ecc = ecc;
    }


#if DEBUG
    /// <summary>
    /// This simulates that an identity requests it's signature key to be added to the block chain.
    /// Remove from code in production.
    /// </summary>
    /// <returns></returns>
    [HttpGet("Simulator")]
    public async Task<ActionResult> GetSimulator(string hobbitDomain)
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit(new AsciiDomainName(hobbitDomain));
        return await hobbit.InitiateRequestNotary(this);
    }

    [HttpGet("SimulatorVerifyBlockChain")]
    public async Task<ActionResult> GetSimulatorVerifyBlockChain()
    {
        await Task.Delay(1);
        await NotaryDatabaseUtil.VerifyEntireBlockChainAsync(_db);

        return Ok();
    }
#endif

    public class VerifyResult
    {
        public UnixTimeUtc keyCreatedTime { get; set; }
    }

    private JsonSerializerOptions options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public class VerifyKeyResult
    {
        public string requestor { get; set; }
        public UnixTimeUtc signatureCreatedTime { get; set; }

        public VerifyKeyResult() { requestor = ""; }
    }

    /// <summary>
    /// This service takes a notary public signature and returns a JSON if it exists.
    /// </summary>
    [HttpGet("GetVerifyNotarizedDocument")]
    public async Task<ActionResult> GetVerifyNotarizedDocument(string notariusPublicusSignatureBase64)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(notariusPublicusSignatureBase64);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        if ((bytes.Length < 10) || (bytes.Length > 128))
            return BadRequest("Too large or too small");

        var record = await _db.NotaryChain.GetAsync(bytes);

        if (record == null)
            return NotFound();

        var result = new VerifyKeyResult()
        {
            requestor = record.identity,
            signatureCreatedTime = record.timestamp.seconds
        };

        return Ok(result);

    }

    private async Task<NotaryChainRecord> TryGetLastLinkOrThrowAsync()
    {
        try
        {
            NotaryChainRecord? record = await _db.NotaryChain.GetLastLinkAsync();

            if (record == null)
                throw new Exception("Block chain appears to be empty");

            return record;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to retrieve the last link: {ex.Message}", ex);
        }
    }

    private void CleanupPendingRegistrationCache()
    {
        UnixTimeUtc now = UnixTimeUtc.Now();

        foreach (var entry in _pendingRegistrationCache)
        {
            if (now.seconds - entry.Value.timestamp.seconds > 60)
                _pendingRegistrationCache.TryRemove(entry.Key, out _);
        }
    }

    public class NotarizeBeginModel
    {
        [Required]
        public string RequestorIdentity { get; set; }

        [Required]
        public string SignedEnvelopeJson { get; set; }
        public NotarizeBeginModel() { SignedEnvelopeJson = ""; RequestorIdentity = ""; }
    }

    /// <summary>
    /// 010. Deserialize the json into a signedEnvelope class
    /// 020. Verify the envelope:
    ///        Verify it's a document or contract (don't know yet if this makes sense)
    ///        Verify at least one signature
    ///        Verify no Notary Public already present
    ///        Verify validity of all embedded signatures
    ///        TODO Call each signatory to verify match of their current public signing key
    ///        TODO Call the KeyChain service to verify proper registration of each public signing key
    /// 030. Verify the requestor has a public key to make sure it's a reasonably valid request - right now the requestor doesn't have to be a signatory.
    /// 040. Limit number of notary public requests
    /// 050. Notarize the document
    /// 060. Server fetches last row
    /// 070. Store in memory dictionary and return previousHash
    ///
    /// Next step for the caller is to (quickly) sign the previousHash and call "Complete"
    /// </summary>
    /// <param name="signedRegistrationInstructionEnvelopeJson"></param>
    /// <returns></returns>
    [HttpPost("NotaryRegistrationBegin")]
    public async Task<ActionResult> PostNotaryRegistrationBegin([FromBody] NotarizeBeginModel model)
    {
        SignedEnvelope? signedEnvelope;

        if (model.SignedEnvelopeJson == null)
            return BadRequest($"Blank envelope");

        AsciiDomainName requestor;
        try
        {
            requestor = new AsciiDomainName(model.RequestorIdentity);
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid requestor identity {ex.Message}");
        }

        // 010. Deserialize the JSON
        //
        try
        {
            signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(model.SignedEnvelopeJson);
        }
        catch (Exception ex)
        {
            return BadRequest($"Can't deserialize: {ex.Message}");
        }
        if (signedEnvelope == null)
            return BadRequest($"Unable to deserialize envelope");

        // 020. Verify envelope
        if ((signedEnvelope.Envelope.EnvelopeType != EnvelopeData.EnvelopeTypeDocument) && (signedEnvelope.Envelope.EnvelopeType != EnvelopeData.EnvelopeTypeContract))
            return BadRequest($"Envelope type must be {EnvelopeData.EnvelopeTypeInstruction} or {EnvelopeData.EnvelopeTypeContract}");
        if (signedEnvelope.Signatures.Count < 1)
            return BadRequest($"Expecting at least one signature, but found {signedEnvelope.Signatures.Count}");
        if (signedEnvelope.NotariusPublicus != null)
            return BadRequest($"Envelope already has a notary public section");
        if (signedEnvelope.VerifyEnvelopeSignatures() == false)
            return BadRequest($"Unable to verify the signatures");

        // TODO: Call each signatory and check their public key matches
        // TODO: Call the keyChain service and check that the public key is registered

        // 030. Fetch the requestor's public key to validate authenticity of the request
        //
        var _httpClient = _httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://" + requestor.DomainName);

        EccPublicKeyData publicKey;

        try
        {
            // Get the requestor's public ECC key for signing
            // /api/v1/PublicKey/SignatureValidation
            string publicKeyJwkBase64Url;

            if (_simulate)
            {
                var hobbit = HobbitSimulator.GetSimulatedHobbit(requestor);
                publicKeyJwkBase64Url = hobbit!.GetPublicKey();
            }
            else
            {
                var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignatureValidation");
                publicKeyJwkBase64Url = await response.Content.ReadAsStringAsync();
            }

            publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(publicKeyJwkBase64Url);
        }
        catch (Exception e)
        {
            return BadRequest($"Getting the public key of [api.{requestor.DomainName}] failed: {e.Message}");
        }

        //
        // 040 We check that a requestor cannot insert too many public keys, e.g. max one per month
        //
        var rList = await _db.NotaryChain.GetIdentityAsync(requestor.DomainName);
        if ((rList != null) && (rList.Count >= 1))
        {
            int count = 0;

            for (int i = 0; i < rList.Count; i++)
            {
                var d = UnixTimeUtc.Now().seconds - rList[rList.Count - 1].timestamp.seconds;

                if (d < 3600 * 24 * 30)
                    count++;

            }
            if (count >= 2)
                return StatusCode(429, "Try again later: at most 2 notarizations per identity per 30 days.");
        }

        // 050 Notarize the document
        try
        {
            signedEnvelope.SignNotariusPublicus(_notaryIdentity, _pwd, _ecc);
            signedEnvelope.VerifyNotariusPublicus();
        }
        catch (Exception e)
        {
            return Problem($"Unable to notarize your document {e.Message}");
        }


        // 060 Retrieve the previous row (we need it's hash to sign)
        NotaryChainRecord lastRowRecord;
        try
        {
            lastRowRecord = await TryGetLastLinkOrThrowAsync();
        }
        catch (Exception ex)
        {
            return Problem(ex.Message);
        }

        // 070 Store in memory cache and return the last record's recordHash (which becomes the new record's previousHash)
        var previousHashBase64 = lastRowRecord.recordHash.ToBase64();

        var preregisteredEntry = new PendingRegistrationData(signedEnvelope, previousHashBase64, requestor, publicKey.PublicKeyJwkBase64Url());
        if (_pendingRegistrationCache.TryAdd(signedEnvelope.Envelope.ContentNonce.ToBase64(), preregisteredEntry) == false)
            return BadRequest("You appear to have sent a duplicate request");

        CleanupPendingRegistrationCache(); // Remove any old cache entries
        return Ok(previousHashBase64);
    }


    public class NotarizeCompleteModel
    {
        [Required]
        public string EnvelopeIdBase64 { get; set; }

        [Required]
        public string SignedPreviousHashBase64 { get; set; }

        public NotarizeCompleteModel() { EnvelopeIdBase64 = ""; SignedPreviousHashBase64 = ""; }
    }

    /// <summary>
    /// 060. Server optimistically calls Client.SignNonce(tempcode, previousHash)
    /// 070. Client returns signedNonce (signed previousHash)
    /// 080. Server verifies signature
    ///
    /// 100. Server serializes each request from hereon in semaphore
    /// 110. Server checks if GetLastLink() has a matching previousHash, if not release the semaphore and retry in step 50 (max 2 retries)
    /// 120. Server calculates new block chain row recordHash
    /// 130. Server verifies row data (hash and maybe timestamp)
    /// 140. Server writes row
    /// 150. Server frees semaphore
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="signedInstructionEnvelopeJson"></param>
    /// <returns></returns>
    [HttpPost("NotaryRegistrationComplete")]
    public async Task<ActionResult> PostNotaryRegistrationComplete([FromBody] NotarizeCompleteModel model)
    {
        if ((model.EnvelopeIdBase64 == null) || (model.SignedPreviousHashBase64 == null))
            return BadRequest("Missing data in model");

        if (_pendingRegistrationCache.TryRemove(model.EnvelopeIdBase64, out var preregisteredEntry) == false)            // Remember to re-insert where a retry is valid
            return NotFound("No such ID found");

        if (UnixTimeUtc.Now().seconds - preregisteredEntry.timestamp.seconds > 60)
            return BadRequest("Expired. Too old");

        var newRecordToInsert = new NotaryChainRecord()
        {
            publicKeyJwkBase64Url = preregisteredEntry.requestorPublicKeyJwkBase64,
            identity = preregisteredEntry.requestor.DomainName,
            previousHash = Convert.FromBase64String(preregisteredEntry.previousHashBase64),
            timestamp = UnixTimeUtc.Now(),
            notarySignature = preregisteredEntry.envelope.NotariusPublicus.Signature
        };

        if (simulateTime != 0)
            newRecordToInsert.timestamp = new UnixTimeUtc(simulateTime.milliseconds);

        try  // Finally is the Semaphore release
        {
            //
            // 0100 - get ready to insert into the blockchain, serialize here
            //
            await _semaphore.WaitAsync();

            // 0110 Retrieve the previous row
            NotaryChainRecord lastRowRecord;
            try
            {
                lastRowRecord = await TryGetLastLinkOrThrowAsync();
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }

            if (ByteArrayUtil.EquiByteArrayCompare(newRecordToInsert.previousHash, lastRowRecord.recordHash) == false)
            {
                preregisteredEntry.previousHashBase64 = lastRowRecord.recordHash.ToBase64();
                if (_pendingRegistrationCache.TryAdd(model.EnvelopeIdBase64, preregisteredEntry) == true)
                    return StatusCode(429, preregisteredEntry.previousHashBase64); // Return "Try again" and the new hash value to try
                else
                    return Problem("Start over, unable to add back in");
            }

            newRecordToInsert.signedPreviousHash = Convert.FromBase64String(model.SignedPreviousHashBase64);

            // 0120 Verify the signed previousHash signature by requestor
            var publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(preregisteredEntry.requestorPublicKeyJwkBase64);
            if (publicKey.VerifySignature(ByteArrayUtil.Combine("Notarize-".ToUtf8ByteArray(), newRecordToInsert.previousHash), newRecordToInsert.signedPreviousHash) == false)
                return BadRequest("Requestor previousHash signature invalid.");

            if (ByteArrayUtil.EquiByteArrayCompare(lastRowRecord.recordHash, newRecordToInsert.previousHash) == false)
                return Problem("Impossible hash mismatch");

            // 130 calculate new hash
            newRecordToInsert.recordHash = NotaryDatabaseUtil.CalculateRecordHash(newRecordToInsert);

            // 140 verify record
            if (NotaryDatabaseUtil.VerifyBlockChainRecord(newRecordToInsert, lastRowRecord, simulateTime == 0) == false)
            {
                return Problem("Cannot verify");
            }

            // 150 write row
            try
            {
                await _db.NotaryChain.InsertAsync(newRecordToInsert);
            }
            catch (Exception e)
            {
                return Problem($"Did you try to register a duplicate? {e.Message}");
            }
            return Ok(preregisteredEntry.envelope.GetCompactSortedJson());
        }
        catch (Exception ex)
        {
            return Problem($"Unexpected: {ex.Message}");
        }
        finally
        {
            // 150 free semaphore
            _semaphore.Release();
        }
    }
}