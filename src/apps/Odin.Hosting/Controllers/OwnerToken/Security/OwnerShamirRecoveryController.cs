using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.ShamiraPasswordRecovery;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.ShamirRecoveryV1)]
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

    [HttpGet("verify")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string id, [FromQuery] string token)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        // OdinValidationUtils.AssertNotNullOrEmpty(redirect, nameof(redirect));

        await _recoveryService.EnterRecoveryMode(Guid.Parse(id), token, WebOdinContext);
        
        const string redirect = "/owner/shamir-account-recovery?fv=1";
        return Redirect(redirect);
    }

    [HttpPost("initiate-recovery-mode")]
    public async Task<IActionResult> InitiateRecoveryMode()
    {
        await _recoveryService.EnterRecoveryMode(WebOdinContext);
        return Ok();
    }
}