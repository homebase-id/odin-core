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
        /// This simulates that frodobaggins.me makes a request for an attestation, remove for production
        /// and put this functionality into the identity host when requesting an attestation.
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            return await SimulateFrodo.InitiateRequestForAttestation(this);
        }


        /// <summary>
        /// This is the endpoint that an identity calls with a signedEnvelope contract of the 
        /// attestations that the identity would like to have made.
        /// </summary>
        /// <param name="requestSignedEnvelope"></param>
        /// <returns></returns>
        [HttpGet("RequestAttestation")]
        public async Task<IActionResult> GetRequestAttestation(string requestSignedEnvelope)
        {
            // First verify the validity of the signed envelope
            SignedEnvelope? signedEnvelope;
            try
            {
                signedEnvelope = InstructionSignedEnvelope.VerifyInstructionEnvelope(requestSignedEnvelope);
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

            // Ok, now we know for certain that the request came from the same identity
            // We know it's a valid request (envelope)
            // (we could later add more details to the request document)
            //

            // Save request in database for later administrative staff review
            //
            var r = new AttestationRequestRecord() { identity = requestorId.DomainName, requestEnvelope = signedEnvelope.GetCompactSortedJson(), timestamp = UnixTimeUtc.Now() };

            try
            {
                if (_db.tblAttestationRequest.Upsert(r) < 1)
                    return BadRequest($"Had trouble upserting row into database, try again");
            }
            catch (Exception  ex)
            {
                return BadRequest($"There was an error: {ex.Message}");
            }

            await Task.Delay(0); // Only to not get into async hell.

            // This means the request has been successfully registered
            return Ok("");
        }
    }
}
