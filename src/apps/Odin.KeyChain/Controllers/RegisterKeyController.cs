// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Util;
using Odin.KeyChain;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using System.Text.Json;
using Odin.Core.Time;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Odin.Keychain
{
    [ApiController]
    [Route("[controller]")]
    public class RegisterKeyController : ControllerBase
    {
        private readonly ILogger<RegisterKeyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly KeyChainDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly bool _simulate = true;
        public static UnixTimeUtc simulateTime = 0;
        private ConcurrentDictionary<string, PendingRegistrationData> _pendingRegistrationCache;

        public RegisterKeyController(ILogger<RegisterKeyController> logger, IHttpClientFactory httpClientFactory, KeyChainDatabase db, ConcurrentDictionary<string, PendingRegistrationData> preregisteredCache)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _pendingRegistrationCache = preregisteredCache;
        }


#if DEBUG
        /// <summary>
        /// This simulates that an identity requests it's signature key to be added to the block chain.
        /// Remove from code in production.
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<ActionResult> GetSimulator(string hobbitDomain = "frodobaggins.me")
        {
            var hobbit = HobbitSimulator.GetSimulatedHobbit(new AsciiDomainName(hobbitDomain));

            return await hobbit.InitiateRequestForKeyRegistration(this);
        }
#endif

        public class VerifyResult
        {
            public long keyCreatedTime { get; set; }
        }

        /// <summary>
        /// This service takes an identity as parameter and returns the age of the identity
        /// </summary>
        /// <param name="identity">An Odin identity</param>
        /// <returns>200 and seconds since Unix Epoch if successful, or NotFound or BadRequest</returns>
        [HttpGet("Verify")]
        public async Task<ActionResult> GetVerify(string identity)
        {
            try
            {
                var id = new AsciiDomainName(identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            using (var conn = _db.CreateDisposableConnection())
            {
                var r = await _db.tblKeyChain.GetOldestAsync(conn, identity);
                if (r == null)
                {
                    return NotFound("No such identity found.");
                }

                var vr = new VerifyResult() { keyCreatedTime = r.timestamp.ToUnixTimeUtc().seconds };

                return Ok(vr);
            }
        }

        private JsonSerializerOptions options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public class VerifyKeyResult
        {
            public long keyCreatedTime { get; set; }
            public long? successorKeyCreatedTime { get; set; }
        }

        /// <summary>
        /// This service takes an identity and it's public key as parameter and checks if it exists.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="PublicKeyJwkBase64Url"></param>
        /// <returns>200 OK and key age in seconds since Unix Epoch, or Bad Request or Not Found</returns>
        [HttpGet("VerifyKey")]
        public async Task<ActionResult> GetVerifyKey(string identity, string PublicKeyJwkBase64Url)
        {
            AsciiDomainName id;
            try
            {
                id = new AsciiDomainName(identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            EccPublicKeyData publicKey;

            try
            {
                publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(PublicKeyJwkBase64Url);
            }
            catch (Exception)
            {
                return BadRequest("Invalid public key");
            }

            using (var conn = _db.CreateDisposableConnection())
            {
                // TODO some snowy day... have multiple entries and find the range
                // for the given key {creationTime to replaceTime}. 
                var list = await _db.tblKeyChain.GetIdentityAsync(conn, id.DomainName);

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].publicKeyJwkBase64Url == PublicKeyJwkBase64Url)
                    {
                        var vr = new VerifyKeyResult() { keyCreatedTime = list[i].timestamp.ToUnixTimeUtc().seconds };
                        if (i + 1 < list.Count)
                            vr.successorKeyCreatedTime = list[i + 1].timestamp.ToUnixTimeUtc().seconds;

                        return Ok(JsonSerializer.Serialize(vr, options));
                    }
                }

                return NotFound();
            }
        }

        private async Task<KeyChainRecord> TryGetLastLinkOrThrowAsync()
        {
            try
            {
                using (var conn = _db.CreateDisposableConnection())
                {

                    KeyChainRecord? record = await _db.tblKeyChain.GetLastLinkAsync(conn);

                    if (record == null)
                        throw new Exception("Block chain appears to be empty");

                    return record;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to retrieve the last link: {ex.Message}", ex);
            }
        }


        public class RegistrationBeginModel
        {
            [Required]
            public string SignedRegistrationInstructionEnvelopeJson { get; set; }
            public RegistrationBeginModel() { SignedRegistrationInstructionEnvelopeJson = ""; }
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

        /// <summary>
        /// 010. Client calls PublicKeyRegistrationBegin(signedInstruction)
        /// 020. Deserialize the json into a signedEnvelope
        /// 030. Verify the envelope and signature
        /// 040. Server calls Client.GetPublicKey() to verify signature key (important)
        /// 050. Make sure there's at least 30 days between registration attempts
        /// 060. Server fetches last row
        /// 070. Store in memory dictionary and return previousHash
        /// 
        /// Next step for the caller is to (quickly) sign the previousHash and call "Complete"
        /// </summary>
        /// <param name="signedRegistrationInstructionEnvelopeJson"></param>
        /// <returns></returns>
        [HttpPost("PublicKeyRegistrationBegin")]
        public async Task<ActionResult> PostPublicKeyRegistrationBegin([FromBody] RegistrationBeginModel model)
        {
            SignedEnvelope? signedEnvelope;

            if (model.SignedRegistrationInstructionEnvelopeJson == null)
                return BadRequest($"Blank envelope");

            // 020. Deserialize the JSON
            //
            try
            {
                signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(model.SignedRegistrationInstructionEnvelopeJson);
            }
            catch (Exception ex)
            {
                return BadRequest($"Can't deserialize: {ex.Message}");
            }
            if (signedEnvelope == null)
                return BadRequest($"Unable to deserialize envelope");

            // 030. Verify envelope
            if (signedEnvelope.Envelope.EnvelopeType != EnvelopeData.EnvelopeTypeInstruction)
                return BadRequest($"Envelope type must be {EnvelopeData.EnvelopeTypeInstruction}");
            if (signedEnvelope.Envelope.EnvelopeSubType != InstructionSignedEnvelope.ENVELOPE_SUB_TYPE_KEY_REGISTRATION)
                return BadRequest($"Instruction envelope subtype must be {InstructionSignedEnvelope.ENVELOPE_SUB_TYPE_KEY_REGISTRATION}");
            if (signedEnvelope.Signatures.Count != 1)
                return BadRequest($"Expecting precisely one signature, but found {signedEnvelope.Signatures.Count}");
            if (signedEnvelope.VerifyEnvelopeSignatures() == false)
                return BadRequest($"Unable to verify the signature");

            var domain = new AsciiDomainName(signedEnvelope.Signatures[0].Identity);

            // 040. Fetch the requestor's public key to validate authenticity of the request and domain name.
            //
            var _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri("https://" + domain.DomainName);

            EccPublicKeyData publicKey;

            try
            {
                // Get the public ECC key for signing
                // /api/v1/PublicKey/SignatureValidation
                string publicKeyJwkBase64Url;

                if (_simulate)
                {
                    var hobbit = HobbitSimulator.GetSimulatedHobbit(domain);
                    publicKeyJwkBase64Url = hobbit.GetPublicKey();
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
                return BadRequest($"Getting the public key of [api.{domain.DomainName}] failed: {e.Message}");
            }

            // Validate that the public key is the same as in the request
            if (signedEnvelope.Signatures[0].PublicKeyJwkBase64Url != publicKey.PublicKeyJwkBase64Url())
                return BadRequest($"The public key of [{domain.DomainName}] didn't match the public key in the instruction envelope");

            using (var conn = _db.CreateDisposableConnection())
            {
                //
                // 050 We check that an idenity cannot insert too many public keys, e.g. max one per month
                //
                var r = await _db.tblKeyChain.GetOldestAsync(conn, domain.DomainName);
                if (r != null)
                {
                    var d = UnixTimeUtc.Now().seconds - r.timestamp.ToUnixTimeUtc().seconds;

                    if (d < 3600 * 24 * 30)
                        return StatusCode(429, "Try again later: at least 30 days between registrations");
                }

                // 060 Retrieve the previous row (we need it's hash to sign)
                KeyChainRecord lastRowRecord;
                try
                {
                    lastRowRecord = await TryGetLastLinkOrThrowAsync();
                }
                catch (Exception ex)
                {
                    return Problem(ex.Message);
                }

                // 070 Store it and return the last record's recordHash (which becomes the new record's previousHash)
                var previousHashBase64 = lastRowRecord.recordHash.ToBase64();

                var preregisteredEntry = new PendingRegistrationData(signedEnvelope, previousHashBase64);
                if (_pendingRegistrationCache.TryAdd(signedEnvelope.Envelope.ContentNonce.ToBase64(), preregisteredEntry) == false)
                    return BadRequest("You appear to have sent a duplicate request");

                CleanupPendingRegistrationCache(); // Remove any old cache entries
                return Ok(previousHashBase64);
            }
        }


        public class RegistrationCompleteModel
        {
            [Required]
            public string EnvelopeIdBase64 { get; set; }

            [Required]
            public string SignedPreviousHashBase64 { get; set; }

            public RegistrationCompleteModel() { EnvelopeIdBase64 = ""; SignedPreviousHashBase64 = ""; }
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
        [HttpPost("PublicKeyRegistrationComplete")]
        public async Task<ActionResult> PostPublicKeyRegistrationComplete([FromBody] RegistrationCompleteModel model)
        {
            if ((model.EnvelopeIdBase64 == null) || (model.SignedPreviousHashBase64 == null))
                return BadRequest("Missing data in model");

            if (_pendingRegistrationCache.TryRemove(model.EnvelopeIdBase64, out var preregisteredEntry) == false)            // Remember to re-insert where a retry is valid
                return NotFound("No such ID found");

            if (UnixTimeUtc.Now().seconds - preregisteredEntry.timestamp.seconds > 60)
                return BadRequest("Expired. Too old");

            var newRecordToInsert = new KeyChainRecord()
            {
                publicKeyJwkBase64Url = preregisteredEntry.envelope.Signatures[0].PublicKeyJwkBase64Url,
                identity = preregisteredEntry.envelope.Signatures[0].Identity,
                previousHash = Convert.FromBase64String(preregisteredEntry.previousHashBase64),
                timestamp = UnixTimeUtcUnique.Now()
            };

            if (simulateTime != 0)
                newRecordToInsert.timestamp = new UnixTimeUtcUnique(simulateTime.milliseconds << 16);

            try  // Finally is the Semaphore release
            {
                //
                // 0100 - get ready to insert into the blockchain, serialize here
                //
                await _semaphore.WaitAsync();

                // 0110 Retrieve the previous row
                KeyChainRecord lastRowRecord;
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


                // 0120 Verify the signed previousHash
                var publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(preregisteredEntry.envelope.Signatures[0].PublicKeyJwkBase64Url);
                if ( publicKey.VerifySignature(ByteArrayUtil.Combine("PublicKeyChain-".ToUtf8ByteArray(), newRecordToInsert.previousHash), newRecordToInsert.signedPreviousHash) == false)
                    return BadRequest("Signature invalid.");


                if (ByteArrayUtil.EquiByteArrayCompare(lastRowRecord.recordHash, newRecordToInsert.previousHash) == false)
                    return Problem("Impossible hash mismatch");

                // 130 calculate new hash
                newRecordToInsert.recordHash = KeyChainDatabaseUtil.CalculateRecordHash(newRecordToInsert);

                // 140 verify record
                if (KeyChainDatabaseUtil.VerifyBlockChainRecord(newRecordToInsert, lastRowRecord, simulateTime == 0) == false)
                {
                    return Problem("Cannot verify");
                }

                using (var conn = _db.CreateDisposableConnection())
                {
                    // 150 write row
                    try
                    {
                        await _db.tblKeyChain.InsertAsync(conn, newRecordToInsert);
                    }
                    catch (Exception e)
                    {
                        return Problem($"Did you try to register a duplicate? {e.Message}");
                    }
                    return Ok("OK");
                }
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
}
