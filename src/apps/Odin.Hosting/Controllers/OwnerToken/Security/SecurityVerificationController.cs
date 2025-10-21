using System;
using System.Threading.Tasks;
using Bitcoin.BIP39;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security;
using Odin.Services.Security.Health;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

[ApiController]
[Route(OwnerApiPathConstants.SecurityRecoveryV1)]
[AuthorizeValidOwnerToken]
public class SecurityVerificationController(OwnerSecurityHealthService securityHealthService) : OdinControllerBase
{
    [HttpPost("verify-password")]
    public async Task<IActionResult> VerifyPassword([FromBody] PasswordReply package)
    {
        WebOdinContext.Caller.AssertHasMasterKey();
        await securityHealthService.VerifyPasswordAsync(package, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-recovery-key")]
    public async Task<IActionResult> VerifyRecoveryKey([FromBody] VerifyRecoveryKeyRequest reply)
    {
        try
        {
            WebOdinContext.Caller.AssertHasMasterKey();
            await securityHealthService.VerifyRecoveryKeyAsync(reply, WebOdinContext);
        }
        catch (BIP39Exception e)
        {
            throw new OdinSecurityException("BIP39 failed", e);
        }

        return new OkResult();
    }

    [HttpGet("recovery-info")]
    public async Task<RecoveryInfo> GetRecoveryInfo([FromQuery] bool live = false)
    {
        return await securityHealthService.GetRecoveryInfo(live, WebOdinContext);
    }

    [HttpPost("update-recovery-email")]
    public async Task<IActionResult> UpdateRecoveryEmail([FromBody] UpdateRecoveryEmailRequest request)
    {
        await securityHealthService.StartUpdateRecoveryEmail(request.Email, request.PasswordReply, WebOdinContext);
        return Ok();
    }
    
    [HttpGet("needs-attention")]
    public async Task<bool> RecoveryNeedsAttention()
    {
        var recoveryInfo = await securityHealthService.GetRecoveryInfo(live: false, WebOdinContext);

        if (recoveryInfo is null)
            return true;

        if (!recoveryInfo.IsConfigured ||
            string.IsNullOrEmpty(recoveryInfo.Email) ||
            !recoveryInfo.EmailLastVerified.HasValue ||
            !recoveryInfo.RecoveryRisk.IsRecoverable)
            return true;

        var maxWait = TimeSpan.FromDays(30 * 6);
        var now = DateTime.UtcNow;

        bool IsStale(DateTime dt) => now - dt.ToUniversalTime() > maxWait;

        if (IsStale(recoveryInfo.EmailLastVerified.Value.ToDateTime()) ||
            IsStale(recoveryInfo.Status.RecoveryKeyLastVerified.ToDateTime()) ||
            IsStale(recoveryInfo.Status.PasswordLastVerified.ToDateTime()))
            return true;

        return false;
    }


    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyRecoveryEmail([FromQuery] string id)
    {
        await securityHealthService.FinalizeUpdateRecoveryEmail(Guid.Parse(id), WebOdinContext);
        
        const string redirect = "/owner/security/overview?fv=1";
        return Redirect(redirect);
    }

    [HttpGet("recovery-risk-report")]
    public async Task<IActionResult> GetHealthCheck()
    {
        var check = await securityHealthService.RunHealthCheck(WebOdinContext);
        return Ok(check);
    }
}