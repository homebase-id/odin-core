using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.Database.Attestation;
using Odin.Core.Storage.Database.Attestation.Table;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Attestation.Controllers;

[ApiController]
[Route("request")]
public class AttestationRequestController : ControllerBase
{
    private readonly ILogger<AttestationRequestController> _logger;
    private readonly AttestationDatabase _db;
    private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    //private readonly bool _simulate = true;
    private readonly EccFullKeyData _eccKey;
    private readonly SensitiveByteArray _eccPwd;

    public AttestationRequestController(ILogger<AttestationRequestController> logger, AttestationDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
    {
        _logger = logger;
        _db = db;
        _eccKey = eccKey;
        _eccPwd = pwdEcc;
    }


    private static string GetIdentityPublicKey(AsciiDomainName identity)
    {
        // @Todd - Here make an HTTP call instead of the simulation
        var jwkBase64Url = SimulateFrodo.GetPublicKey();

        return jwkBase64Url;
    }

#if DEBUG
    /// <summary>
    /// This simulates that frodobaggins.me makes a request for an attestation, remove for production
    /// and put this functionality into the identity host when requesting an attestation.
    /// </summary>
    /// <returns></returns>
    [HttpGet("Simulator")]
    public async Task<ActionResult> GetSimulator()
    {
        return await SimulateFrodo.InitiateRequestForAttestation(this);
    }
#endif

    public class VerifyAttestationResult
    {
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc? modified { get; set; }
        public Int32 status { get; set; }
    };

    private JsonSerializerOptions options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// This service takes an identity and an attestationId and checks if it exists, returns seconds created (UnixEpoch).
    /// </summary>
    /// <param name="attestationIdBase64"></param>
    /// <returns>200 OK and attesation age in seconds (Unix Epoch), or Bad Request or Not Found</returns>
    [HttpGet("VerifyAttestation")]
    public async Task<ActionResult> GetVerifyAttestaion(string attestationIdBase64)
    {
        byte[] attestationId;

        try
        {
            attestationId = Convert.FromBase64String(attestationIdBase64);
        }
        catch (Exception)
        {
            return BadRequest("Invalid attestationIdBase64");
        }

        var r = await _db.AttestationStatus.GetAsync(attestationId);
        if (r == null)
        {
            return NotFound("No such attestationId found.");
        }

        var result = new VerifyAttestationResult() { created = r.created.seconds, modified = r.modified.seconds, status = r.status };

        return Ok(JsonSerializer.Serialize(result, options));

    }

    /// <summary>
    /// This is the endpoint that an identity calls with a signedEnvelope contract of the
    /// attestations that the identity would like to have made.
    /// </summary>
    /// <param name="requestSignedEnvelope"></param>
    /// <returns></returns>
    [HttpGet("RequestAttestation")]
    public async Task<ActionResult> GetRequestAttestation(string requestSignedEnvelope) // TODO: Change to POST
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
        var requestorId = new AsciiDomainName(signedEnvelope.Signatures[0].Identity);

        // Let's fetch the identity's public key and make sure it's the same
        // This would be a web service call to Frodo
        //
        var publicKeyBase64Url = GetIdentityPublicKey(requestorId);

        if (publicKeyBase64Url != signedEnvelope.Signatures[0].PublicKeyJwkBase64Url)
            return BadRequest($"Identity public key does not match the request");

        // Ok, now we know for certain that the request came from the same identity
        // We know it's a valid request (envelope)
        // (we could later add more details to the request document)
        //

        // Save request in database for later administrative staff review
        //
        var r = new AttestationRequestRecord() { attestationId = signedEnvelope.Envelope.ContentNonce.ToBase64(), requestEnvelope = signedEnvelope.GetCompactSortedJson(), timestamp = UnixTimeUtc.Now() };

        try
        {
            if (await _db.AttestationRequest.UpsertAsync(r) < 1)
                return BadRequest($"Had trouble upserting row into database, try again");
        }
        catch (Exception ex)
        {
            return BadRequest($"There was an error: {ex.Message}");
        }

        await Task.Delay(0); // Only to not get into async hell.

        // This means the request has been successfully registered
        return Ok("");
    }

}