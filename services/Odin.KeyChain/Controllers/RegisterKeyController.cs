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

        public RegisterKeyController(ILogger<RegisterKeyController> logger, IHttpClientFactory httpClientFactory, KeyChainDatabase db)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
        }


#if DEBUG
        /// <summary>
        /// This simulates that an identity requests it's signature key to be added to the block chain.
        /// Remove from code in production.
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            return await SimulateFrodo.InitiateRequestForKeyRegistration(this);
        }
#endif

        /// <summary>
        /// This service takes an identity as parameter and returns the age of the identity
        /// </summary>
        /// <param name="identity">An Odin identity</param>
        /// <returns>200 and seconds since Unix Epoch if successful, or NotFound or BadRequest</returns>
        [HttpGet("Verify")]
        public IActionResult GetVerify(string identity)
        {
            try
            {
                var id = new PunyDomainName(identity);
            }
            catch (Exception ex) {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            var r = _db.tblKeyChain.Get(identity);
            if (r == null)
            {
                return NotFound("No such identity found.");
            }

            var msg = $"{r.timestamp.ToUnixTimeUtc().milliseconds / 1000}";

            return Ok(msg);
        }


        /// <summary>
        /// This service takes an identity and it's public key as parameter and checks if it exists.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="publicKeyDerBase64"></param>
        /// <returns>200 OK and key age in seconds since Unix Epoch, or Bad Request or Not Found</returns>
        [HttpGet("VerifyKey")]
        public IActionResult GetVerifyKey(string identity, string publicKeyDerBase64)
        {
            try
            {
                var id = new PunyDomainName(identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            EccPublicKeyData publicKey;

            try
            {
                var publicKeyDerBytes = Convert.FromBase64String(publicKeyDerBase64);
                publicKey = EccPublicKeyData.FromDerEncodedPublicKey(publicKeyDerBytes);
            }
            catch(Exception)
            {
                return BadRequest("Invalid public key");
            }

            var r = _db.tblKeyChain.Get(identity, publicKey.publicKey);
            if (r == null)
            {
                return NotFound("No such identity,key found.");
            }

            var msg = $"{r.timestamp.ToUnixTimeUtc().milliseconds / 1000} key registration";

            return Ok(msg);
        }

        private KeyChainRecord TryGetLastLinkOrThrow()
        {
            try
            {
                KeyChainRecord? record = _db.tblKeyChain.GetLastLink();

                if (record == null)
                    throw new Exception("Block chain appears to be empty");

                return record;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to retrieve the last link: {ex.Message}", ex);
            }
        }


        /// <summary>
        /// 010. Client calls Server.RegisterKey(identity, signedInstruction)
        /// 020. Deserialize the json into a signedEnvelope
        /// 030. Verify the envelope
        /// 040. Server calls Client.GetPublicKey() to get signature key (strictly speaking not needed)
        /// 
        /// 050. Server fetches last row
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
        [HttpGet("Register")]
        public async Task<IActionResult> GetRegister(string identity, string signedInstructionEnvelopeJson)
        {
            const int MAX_TRIES = 3;
            int attemptCount = 0;

            // 010. Initialize and ensure high level integrity
            //
            PunyDomainName domain;
            try
            {
                domain = new PunyDomainName(identity);
            }
            catch (Exception)
            {
                return BadRequest("Invalid identity, not a proper puny-code domain name");
            }

            if ((signedInstructionEnvelopeJson.Length < 10) || (signedInstructionEnvelopeJson.Length > 65000))
            {
                return BadRequest("Invalid envelope, needs to be [10..65000] characters");
            }


            SignedEnvelope? signedEnvelope;

            // 020. Deserialize the JSON
            //
            try
            {
                signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(signedInstructionEnvelopeJson);
            }
            catch (Exception ex)
            {
                return BadRequest($"{ex.Message}");
            }

            if (signedEnvelope == null)
                return BadRequest($"Unable to deserialize envelope");

            // 030. Verify envelope
            if (signedEnvelope.Envelope.EnvelopeType != EnvelopeData.EnvelopeTypeInstruction)
                return BadRequest($"Envelope type must be {EnvelopeData.EnvelopeTypeInstruction}");
            if (signedEnvelope.Envelope.EnvelopeSubType != InstructionSignedEnvelope.ENVELOPE_SUB_TYPE_KEY_REGISTRATION)
                return BadRequest($"Instruction envelope subtype must be {InstructionSignedEnvelope.ENVELOPE_SUB_TYPE_KEY_REGISTRATION}");

            // Begin building a block chain records to insert...
            //
            var newRecordToInsert = KeyChainDatabaseUtil.NewBlockChainRecord();
            newRecordToInsert.identity = identity;

            // First be sure we can get the caller's public key so we
            // don't block the semaphore needlessly
            //
            var _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri("https://" + domain.DomainName);

            EccPublicKeyData publicKey;

            try
            {
                // 040. Get the public ECC key for signing
                // /api/v1/PublicKey/SignatureValidation
                string publicKeyBase64;

                if (_simulate)
                {
                    publicKeyBase64 = SimulateFrodo.GetPublicKey();
                }
                else
                {
                    var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignatureValidation");
                    publicKeyBase64 = await response.Content.ReadAsStringAsync();
                }
                var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
                publicKey = EccPublicKeyData.FromDerEncodedPublicKey(publicKeyBytes);
            }
            catch (Exception e)
            {
                return BadRequest($"Getting the public key of [api.{domain.DomainName}] failed: {e.Message}");
            }

            // Validate that the public key is the same in the request
            if (ByteArrayUtil.EquiByteArrayCompare(signedEnvelope.Signatures[0].PublicKeyDer, publicKey.publicKey) == false)
                return BadRequest($"The public key of [{domain.DomainName}] didn't match the public key in the instruction envelope");

            // Add the public key DER to the block chain record
            newRecordToInsert.publicKey = publicKey.publicKey;

            //
            // We could check that an idenity cannot insert too many keys, e.g. max one per month
            //
            var r = _db.tblKeyChain.Get(domain.DomainName);
            if (r != null)
            {
                var d = UnixTimeUtc.Now().seconds - r.timestamp.ToUnixTimeUtc().seconds;

                if (d > 3600 * 24 * 7)
                {
                    throw new Exception("Try again in a week's time");
                }

            }

            // 050 Retrieve the previous row (we need it's hash to sign)
            KeyChainRecord previousRowRecord;
            try
            {
                previousRowRecord = TryGetLastLinkOrThrow();
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
            newRecordToInsert.previousHash = previousRowRecord.recordHash;

            // 
            // Begin the optimistic signature -> block chain insert process
            //
            while (attemptCount < MAX_TRIES)
            {
                attemptCount++;

                // 060. Optimistically call out to get the previous Hash signed
                //
                try
                {
                    string signedPreviousHashBase64;

                    if (_simulate)
                    {
                        signedPreviousHashBase64 = SimulateFrodo.SignNonceForKeyChain(newRecordToInsert.previousHash.ToBase64(), signedEnvelope.Envelope.ContentNonce.ToBase64());
                    }
                    else
                    {
                        var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignNonce");
                        signedPreviousHashBase64 = await response.Content.ReadAsStringAsync();
                    }

                    newRecordToInsert.signedPreviousHash = Convert.FromBase64String(signedPreviousHashBase64);

                    // 080
                    if (publicKey.VerifySignature(newRecordToInsert.previousHash, newRecordToInsert.signedPreviousHash) == false)
                    {
                        return BadRequest("Signature invalid.");
                    }
                }
                catch (Exception e)
                {
                    return BadRequest($"Error getting the signed nonce of [api.{domain.DomainName}] failed: {e.Message}");
                }


                try
                {
                    //
                    // 0100 - insert the blockchain, serialize here
                    //
                    await _semaphore.WaitAsync();

                    // 110 Retrieve the previous row AGAIN to make sure it's the same
                    try
                    {
                        previousRowRecord = TryGetLastLinkOrThrow();
                    }
                    catch (Exception ex)
                    {
                        return Problem(ex.Message);
                    }

                    if (ByteArrayUtil.EquiByteArrayCompare(previousRowRecord.recordHash, newRecordToInsert.previousHash) == false)
                    {
                        // oh no, someone beat us to it, try again
                        newRecordToInsert.previousHash = previousRowRecord.recordHash;
                        continue;
                    }

                    // 120 calculate new hash
                    newRecordToInsert.recordHash = KeyChainDatabaseUtil.CalculateRecordHash(newRecordToInsert);

                    // 130 verify record
                    if (KeyChainDatabaseUtil.VerifyBlockChainRecord(newRecordToInsert, previousRowRecord) == false)
                    {
                        return Problem("Cannot verify");
                    }

                    // 140 write row
                    try
                    {
                        _db.tblKeyChain.Insert(newRecordToInsert);
                    }
                    catch (Exception e)
                    {
                        return Problem($"Did you try to register a duplicate? {e.Message}");
                    }
                    _db.Commit(); // Flush immediately

                    return Ok("OK");
                }
                finally
                {
                    // 150 free semaphore
                    _semaphore.Release();
                }
            } // while

            return StatusCode(429, "Please try again later, someone else is too fast.");
        }
    }
}