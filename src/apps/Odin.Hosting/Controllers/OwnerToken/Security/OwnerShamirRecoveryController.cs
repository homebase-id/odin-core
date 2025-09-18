using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityRecoveryV1)]
public class OwnerShamirRecoveryController : OdinControllerBase
{
    private readonly ShamirRecoveryService _recoveryService;

    /// <summary />
    public OwnerShamirRecoveryController(ShamirRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    [HttpGet("status")]
    public async Task<ShamirRecoveryStatusRedacted> GetRecoveryStatus()
    {
        var status = await _recoveryService.GetStatus(WebOdinContext);
        return status;
    }

    [HttpPost("initiate-recovery-mode")]
    public async Task<IActionResult> InitiateRecoveryMode()
    {
        await _recoveryService.InitiateRecoveryModeEntry(WebOdinContext);
        return Ok();
    }

    [HttpGet("verify-enter")]
    public async Task<IActionResult> VerifyEnterRecoveryMode([FromQuery] string id, [FromQuery] string token)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        // OdinValidationUtils.AssertNotNullOrEmpty(redirect, nameof(redirect));

        await _recoveryService.EnterRecoveryMode(Guid.Parse(id), token, WebOdinContext);

        const string redirect = "/owner/shamir-account-recovery?fv=1";
        return Redirect(redirect);
    }

    [HttpPost("exit-recovery-mode")]
    public async Task<IActionResult> InitiateExitRecoveryMode()
    {
        await _recoveryService.InitiateRecoveryModeExit(WebOdinContext);
        return Ok();
    }

    [HttpGet("verify-exit")]
    public async Task<IActionResult> VerifyExitRecoveryMode([FromQuery] string id, [FromQuery] string token)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        await _recoveryService.ExitRecoveryMode(Guid.Parse(id), token, WebOdinContext);

        const string redirect = "/owner/login";
        return Redirect(redirect);
    }

    [HttpPost("finalize")]
    public async Task<FinalRecoveryResult> FinalizeRecovery([FromBody] FinalRecoveryRequest request)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(request.Id, nameof(request.Id));
        return await _recoveryService.FinalizeRecovery(Guid.Parse(request.Id), Guid.Parse(request.FinalKey), WebOdinContext);
    }
}