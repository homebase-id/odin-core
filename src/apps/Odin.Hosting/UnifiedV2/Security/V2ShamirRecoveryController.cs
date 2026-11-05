using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// Recovery with friends for an owner who forgot the password: V2 of <c>OwnerShamirRecoveryController</c>, same service.
/// </summary>
/// <remarks>
/// <para>
/// No login, as on V1. Deliberately no <see cref="UnifiedV2AuthorizeAttribute"/>, not even
/// <c>UnifiedPolicies.Anonymous</c>: that one runs the unified scheme, so a browser with a live owner cookie would have
/// to encrypt its bodies, which the recovery page can't (it has no shared secret), and a browser with a stale one would
/// get a 401, where recovering from it is the whole point. With no scheme run, every caller is anonymous and sends and
/// gets plain JSON, whatever it holds, which is exactly what V1 does.
/// </para>
/// <para>
/// The email's GET links (<c>verify-enter</c>, <c>verify-exit</c>, <c>verify-email-fwd</c>) stay on V1: no client calls
/// them, and mail already sent names V1's paths.
/// </para>
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.SecurityRecovery)]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ShamirRecoveryController(ShamirRecoveryService recoveryService) : OdinControllerBase
{
    [HttpGet("status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ShamirRecoveryStatusRedacted> GetRecoveryStatus()
    {
        return await recoveryService.GetStatus(WebOdinContext);
    }

    [HttpPost("initiate-recovery-mode")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> InitiateRecoveryMode(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] InitiateRecoveryModeRequest request)
    {
        await recoveryService.InitiateRecoveryModeEntry(request?.RecoveryPublicKeyJwk, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-enter")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> EnterRecoveryMode([FromBody] EnterRecoveryModeRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Id, nameof(request.Id));
        await recoveryService.EnterRecoveryMode(Guid.Parse(request.Id), request.RecoveryPublicKeyJwk, WebOdinContext);
        return Ok();
    }

    [HttpPost("fill-slot")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> FillSlot([FromBody] FillSlotRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        // A link a mail client wrapped or cut: say it no longer works, as for any other bad link
        if (!Guid.TryParse(request.ShardId, out var shardId))
        {
            throw new OdinClientException("This link no longer works", OdinClientErrorCode.ShardLinkNoLongerValid);
        }

        await recoveryService.FillSlotAsync(shardId, request.Code, WebOdinContext);
        return Ok();
    }

    [HttpGet("recovery-bundle")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ShamirRecoveryBundleResponse> GetRecoveryBundle()
    {
        return await recoveryService.GetRecoveryBundleAsync(WebOdinContext);
    }

    [HttpPost("exit-recovery-mode")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> InitiateExitRecoveryMode()
    {
        await recoveryService.InitiateRecoveryModeExit(WebOdinContext);
        return Ok();
    }

    [HttpPost("finalize")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> FinalizeRecovery([FromBody] FinalRecoveryRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Secret64, nameof(request.Secret64));
        var secret = new byte[ShamirConfigurationService.DistributionKeyLength];
        OdinValidationUtils.AssertIsTrue(Convert.TryFromBase64String(request.Secret64, secret, out var length) && length == secret.Length,
            $"The secret is {secret.Length} bytes, base64");
        await recoveryService.FinalizeServerMadeDealAsync(secret, request.PasswordReply, WebOdinContext);
        return Ok();
    }
}
