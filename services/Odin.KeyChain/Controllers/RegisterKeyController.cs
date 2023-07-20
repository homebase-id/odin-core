// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using System.Text;
using Odin.Core;
using Odin.Core.Storage.SQLite.BlockChainDatabase;
using Odin.Core.Util;
using Odin.Core.Time;
using Microsoft.Data.Sqlite;
using Odin.KeyChain;
using Odin.Core.Cryptography.Data;

namespace OdinsChains.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class RegisterKeyController : ControllerBase
    {
        private readonly ILogger<RegisterKeyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BlockChainDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly bool _simulate = true;

        public RegisterKeyController(ILogger<RegisterKeyController> logger, IHttpClientFactory httpClientFactory, BlockChainDatabase db)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
        }


        /// <summary>
        /// This simulates that an identity requests it's signature key to be added to the block chain
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            // @Todd first you generate a random temp code
            var tempCode = ByteArrayUtil.GetRndByteArray(32);

            // @Todd then you save it in the IdentityDatabase
            // identityDb.tblKkeyValue.Upsert(CONST_SIGNATURE_TEMPCODE_ID, tempCode);

            // @Todd then you call out over HTTPS to request it
            var r1 = await GetRegister("frodo.baggins.me", Convert.ToBase64String(tempCode));

            // If it's OK 200, then you're done.
            // Done.


            // Do another hacky one for testing
            SimulateFrodo.GenerateNewKeys();
            var r2 = await GetRegister("samwise.gamgee.me", Convert.ToBase64String("some temp code from Sam's server".ToUtf8ByteArray()));

            if (BlockChainDatabaseUtil.VerifyEntireBlockChain(_db) == false)
            {
                return Problem("Fenris broke the chain");
            }

            return r2;
        }

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

            var r = _db.tblBlockChain.Get(identity);
            if (r == null)
            {
                return NotFound("No such identity found.");
            }

            var msg = $"{r.timestamp.ToUnixTimeUtc().milliseconds / 1000}";

            return Ok(msg);
        }


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

            var r = _db.tblBlockChain.Get(identity, publicKey.publicKey);
            if (r == null)
            {
                return NotFound("No such identity,key found.");
            }

            var msg = $"{r.timestamp.ToUnixTimeUtc().milliseconds / 1000} key registration";

            return Ok(msg);
        }


        /// <summary>
        /// 010. Client calls Server.RegisterKey(identity, tempcode)
        /// 020. Server calls Client.GetPublicKey() to get signature key
        /// 030. Server calls Client.SignNonce(tempcode, previousHash)
        /// 033. Client returns signedNonce
        /// 037. Server verifies signature
        /// 040. Server serializes each request from hereon in semaphore
        /// 050. Server fetches last row
        /// 060. Server calculates new block chain row
        /// 070. Server verifies row data (hash and maybe timestamp)
        /// 080. Server writes row
        /// 090. Server frees semaphore
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="tempCode"></param>
        /// <returns></returns>
        [HttpGet("Register")]
        public async Task<IActionResult> GetRegister(string identity, string tempCode)
        {
            PunyDomainName domain;
            try
            {
                domain = new PunyDomainName(identity);
            }
            catch (Exception)
            {
                return BadRequest("Invalid identity, not a proper puny-code domain name");
            }

            if ((tempCode.Length < 16) || (tempCode.Length > 64))
            {
                return BadRequest("Invalid temp-code, needs to be [16..64] characters");
            }


            var newRecordToInsert = BlockChainDatabaseUtil.NewBlockChainRecord();

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

            // 1a. Got a request to register a key, get the previous hash and get it signed
            //     We need to lock here, so that each request is serialized

            newRecordToInsert.publicKey = publicKey.publicKey; // DER encoded


            // 030. Sign nonce

            newRecordToInsert.nonce = ByteArrayUtil.GetRndByteArray(32);

            try
            {
                string signedNonceBase64;

                if (_simulate)
                {
                    signedNonceBase64 = SimulateFrodo.SignNonceForKeyChain(newRecordToInsert.nonce.ToBase64(), tempCode);
                }
                else
                {
                    var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignNonce");
                    signedNonceBase64 = await response.Content.ReadAsStringAsync();
                }

                newRecordToInsert.signedNonce = Convert.FromBase64String(signedNonceBase64);

                // 037
                if (publicKey.VerifySignature(newRecordToInsert.nonce, newRecordToInsert.signedNonce) == false)
                {
                    return BadRequest("Signature invalid.");
                }
            }
            catch (Exception e)
            {
                return BadRequest($"Error getting the signed nonce of [api.{domain.DomainName}] failed: {e.Message}");
            }

            // 040 semaphore
            await _semaphore.WaitAsync();


            try
            {
                // 50 Retrieve the previous row and it's hash
                BlockChainRecord previousRowRecord;

                try
                {
                    previousRowRecord = _db.tblBlockChain.GetLastLink();
                    if (previousRowRecord == null)
                        return Problem("Database is broken");
                }
                catch (Exception)
                {
                    return Problem("Database is broken");
                }

                newRecordToInsert.previousHash = previousRowRecord.recordHash;

                // 060 calculate new hash
                newRecordToInsert.recordHash = BlockChainDatabaseUtil.CalculateRecordHash(newRecordToInsert);

                // 070 verify record
                if (BlockChainDatabaseUtil.VerifyBlockChainRecord(newRecordToInsert, previousRowRecord) == false)
                {
                    return Problem("Cannot verify");
                }

                // 080 write row
                try
                {
                    _db.tblBlockChain.Insert(newRecordToInsert);
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