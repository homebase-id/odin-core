using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.BlockChainDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.KeyChain;
using System.Text.Json;

namespace OdinsAttestation.Controllers
{
    [ApiController]
    [Route("request")]
    public class AttestationRequestController : ControllerBase
    {
        private readonly ILogger<AttestationRequestController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BlockChainDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        //private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public AttestationRequestController(ILogger<AttestationRequestController> logger, IHttpClientFactory httpClientFactory, BlockChainDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
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

        private static byte[] GetIdentityPublicKey(PunyDomainName identity)
        {
            // @Todd - Here make an HTTP call instead of the simulation
            var pk64 = SimulateFrodo.GetPublicKey();

            return Convert.FromBase64String(pk64);
        }

        /// <summary>
        /// This simulates that frodo makes a request for an attestation, remove for production
        /// and put this functionality into the identity server when requesting an attestation
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            // Let's build the envelope that Frodo will send
            var signedEnvelope = SimulateFrodo.RequestEnvelope(null);

            // Call the attestation server via HttpClient(Factory)
            var r1 = await GetRequestAttestation(signedEnvelope.GetCompactSortedJson());

            return r1;
        }


        [HttpGet("RequestAttestation")]
        public async Task<IActionResult> GetRequestAttestation(string requestSignedEnvelope)
        {
            if (requestSignedEnvelope.Length < 10)
                return BadRequest("Expecting an ODIN signed envelope JSON");

            // Deserialize the JSON

            SignedEnvelope? signedEnvelope;
            try
            {
                signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(requestSignedEnvelope);

                if (signedEnvelope == null)
                    return BadRequest($"Unable to parse JSON.");
            }
            catch (Exception  ex)
            {
                return BadRequest($"Unable to parse JSON {ex.Message}");
            }

            // Verify the embedded signature

            if (signedEnvelope.VerifyEnvelopeSignatures() == false)
                return BadRequest($"Unable to verify signatures.");

            PunyDomainName id;
            try
            {
                id = new PunyDomainName(signedEnvelope.Signatures[0].Identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            // Let's fetch the identity's public key and make sure it's the same
            // This would be a web service call to Frodo
            //
            var publicKey = GetIdentityPublicKey(id);

            if (ByteArrayUtil.EquiByteArrayCompare(publicKey, signedEnvelope.Signatures[0].PublicKeyDer) == false)
                return BadRequest($"Identity public key does not match the request");

            if (signedEnvelope.Envelope.ContentType != EnvelopeData.ContentTypeRequest)
                return BadRequest($"ContentType must be 'request'");

            if (signedEnvelope.Signatures[0].TimeStamp < UnixTimeUtc.Now().AddSeconds(-60*20) || signedEnvelope.Signatures[0].TimeStamp > UnixTimeUtc.Now().AddSeconds(+60 * 20))
                return BadRequest($"Your clock is too much out of sync or request too old");

            // Ok, now we know for certain that the request came from Frodo
            // We know it's a valid type of request
            // (we could later add more details to the request document)
            //

            // Save request in database
            //

            // TODO database table
            //
            // Table: requestTable
            //    identity STRING
            //    signedEnvelope STRING
            //    created timestamp (and modified)
            //
            await Task.Delay(1);

            return Ok("");
        }
    }
}
