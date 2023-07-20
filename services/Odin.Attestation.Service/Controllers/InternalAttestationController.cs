// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using System.Text;
using Odin.Core;
using Odin.Core.Storage.SQLite.BlockChainDatabase;
using Odin.Core.Util;
using Odin.Core.Time;
using Microsoft.Data.Sqlite;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Cryptography.Data;

namespace OdinsAttestation.Controllers
{
    [ApiController]
    [Route("internal")]
    public class InternalAttestationController : ControllerBase
    {
        private readonly ILogger<AttestationRequestController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BlockChainDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        // private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public InternalAttestationController(ILogger<AttestationRequestController> logger, IHttpClientFactory httpClientFactory, BlockChainDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _eccKey = eccKey;
            _eccPwd = pwdEcc;
        }


        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="_db"></param>
        public static void InitializeDatabase(BlockChainDatabase _db)
        {
        }


        /// <summary>
        /// This simulates that an identity requests it's signature key to be added to the block chain
        /// </summary>
        /// <returns></returns>
/*        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
        }*/

        [HttpGet("AttestHuman")]
        public IActionResult GetAttestHuman(string requestCode)
        {
            // get identity from 
            var identity = "frodo.com";

            PunyDomainName id;

            try
            {
                id = new PunyDomainName(identity);
            }
            catch (Exception ex) {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            var attestation = AttestationManagement.AttestHuman(_eccKey, _eccPwd, id);


            return Ok(attestation.GetCompactSortedJson());
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

            await Task.Delay(1);

            return Ok("OK");
        }

    }
}