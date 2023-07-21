using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.AttestationDatabase;
using Odin.Core.Time;
using Odin.Core.Util;

namespace OdinsAttestation.Controllers
{
    [ApiController]
    [Route("request")]
    public class AttestationRequestController : ControllerBase
    {
        private readonly ILogger<AttestationRequestController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AttestationDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        //private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public AttestationRequestController(ILogger<AttestationRequestController> logger, IHttpClientFactory httpClientFactory, AttestationDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _eccKey = eccKey;
            _eccPwd = pwdEcc;
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
            var signedEnvelope = SimulateFrodo.RequestEnvelope();

            // Call the attestation server via HttpClient(Factory)
            var r1 = await GetRequestAttestation(signedEnvelope.GetCompactSortedJson());

            return r1;
        }


        [HttpGet("RequestAttestation")]
        public async Task<IActionResult> GetRequestAttestation(string requestSignedEnvelope)
        {
            SignedEnvelope? signedEnvelope;
            try
            {
                signedEnvelope = RequestSignedEnvelope.VerifyRequestEnvelope(requestSignedEnvelope);
            }
            catch (Exception  ex)
            {
                return BadRequest($"{ex.Message}");
            }

            // This will work because it was already validated in the Verify... above
            var requestorId = new PunyDomainName(signedEnvelope.Signatures[0].Identity);

            // Let's fetch the identity's public key and make sure it's the same
            // This would be a web service call to Frodo
            //
            var publicKey = GetIdentityPublicKey(requestorId);

            if (ByteArrayUtil.EquiByteArrayCompare(publicKey, signedEnvelope.Signatures[0].PublicKeyDer) == false)
                return BadRequest($"Identity public key does not match the request");

            // Ok, now we know for certain that the request came from Frodo
            // We know it's a valid type of request
            // (we could later add more details to the request document)
            //

            // Save request in database
            //
            var r = new AttestationRequestRecord() { identity = requestorId.DomainName, requestEnvelope = signedEnvelope.GetCompactSortedJson(), timestamp = UnixTimeUtc.Now() };

            try
            {
                if (_db.tblAttestationRequest.Insert(r) < 1)
                    return BadRequest($"Had trouble inserting row into database, try again");
            }
            catch (Exception  ex)
            {
                return BadRequest($"Your previous request is being processed, wait: {ex.Message}");
            }

            await Task.Delay(0); // Done to not get into async hell.

            return Ok("");
        }
    }
}
