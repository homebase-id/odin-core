// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using System.Text;
using Odin.Core;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Util;
using Odin.Core.Time;
using Microsoft.Data.Sqlite;
using Odin.KeyChain;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using System.Security.Principal;
using System.Text.Json;

namespace OdinsChains.Controllers
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


        /// <summary>
        /// 005. Client calls Server.RegisterKey(identity, signedInstruction)
        /// 010. Deserialize the json into a signedEnvelope
        /// 015. Verify the envelope
        /// 020. Server calls Client.GetPublicKey() to get signature key (strictly speaking not needed)
        /// 030. Server serializes each request from hereon in semaphore
        /// 040. Server fetches last row
        /// 050. Server calls Client.SignNonce(tempcode, previousHash)
        /// 053. Client returns signedNonce (signed previousHash)
        /// 057. Server verifies signature
        /// 060. Server calculates new block chain row
        /// 070. Server verifies row data (hash and maybe timestamp)
        /// 080. Server writes row
        /// 090. Server frees semaphore
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="signedInstructionEnvelopeJson"></param>
        /// <returns></returns>
        [HttpGet("Register")]
        public async Task<IActionResult> GetRegister(string identity, string signedInstructionEnvelopeJson)
        {
            // 05. Initialize and ensure high level integrity
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

            // 10. Deserialize the JSON
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

            // 015. Verify envelope
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
                // 020. Get the public ECC key for signing
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


            // 030 semaphore
            await _semaphore.WaitAsync();

            try
            {
                // 40 Retrieve the previous row (we need it's hash)
                KeyChainRecord previousRowRecord;

                try
                {
                    previousRowRecord = _db.tblKeyChain.GetLastLink();
                    if (previousRowRecord == null)
                        return Problem("Database is broken");
                }
                catch (Exception)
                {
                    return Problem("Database is broken");
                }

                newRecordToInsert.previousHash = previousRowRecord.recordHash;

                // 050. Sign the previous Hash
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

                    // 057
                    if (publicKey.VerifySignature(newRecordToInsert.previousHash, newRecordToInsert.signedPreviousHash) == false)
                    {
                        return BadRequest("Signature invalid.");
                    }
                }
                catch (Exception e)
                {
                    return BadRequest($"Error getting the signed nonce of [api.{domain.DomainName}] failed: {e.Message}");
                }



                // 060 calculate new hash
                newRecordToInsert.recordHash = KeyChainDatabaseUtil.CalculateRecordHash(newRecordToInsert);

                // 070 verify record
                if (KeyChainDatabaseUtil.VerifyBlockChainRecord(newRecordToInsert, previousRowRecord) == false)
                {
                    return Problem("Cannot verify");
                }

                // 080 write row
                try
                {
                    _db.tblKeyChain.Insert(newRecordToInsert);
                }
                catch (Exception e)
                {
                    return Problem($"Did you try to register a duplicate? {e.Message}");
                }
                _db.Commit(); // Flush immediately
            }
            finally
            {
                // 090 free semaphore
                _semaphore.Release();
            }

            return Ok("OK");
        }

    }
}