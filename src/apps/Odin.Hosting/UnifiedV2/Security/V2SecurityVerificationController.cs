using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Login;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Security;
using Odin.Services.Security.Health;
using Odin.Services.Security.Job;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// The security health checks: V2 of <c>SecurityVerificationController</c>, same services, owner console only.
/// </summary>
/// <remarks>
/// The actions marked <c>NoSharedSecret</c> are the ones V1 exempts by path in <c>SharedSecretEncryptionMiddleware</c>,
/// in both directions; V2 goes by attribute, so without these it would ask a client that sends them in the clear for an
/// encrypted body. <c>verify-email</c> stays on V1: it's an email's link, and only V1's owner handler sends a browser
/// without a session to the login page and back.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.SecurityRecovery)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2SecurityVerificationController(
    OwnerSecurityHealthService securityHealthService,
    TenantConfigService tenantConfigService,
    TenantContext tenantContext,
    IJobManager jobManager) : OdinControllerBase
{
    [HttpPost("verify-password")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<IActionResult> VerifyPassword([FromBody] PasswordReply package)
    {
        WebOdinContext.Caller.AssertHasMasterKey();
        await securityHealthService.VerifyPasswordAsync(package, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-recovery-key")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> VerifyRecoveryKey([FromBody] VerifyRecoveryKeyRequest reply)
    {
        await securityHealthService.VerifyRecoveryKeyAsync(reply, WebOdinContext);
        return new OkResult();
    }

    [HttpGet("recovery-info")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<RecoveryInfo> GetRecoveryInfo([FromQuery] bool live = false)
    {
        return await securityHealthService.GetRecoveryInfo(live, WebOdinContext);
    }

    [HttpPost("update-monthly-security-health-report-status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<IActionResult> UpdateMonthlyReportStatus([FromQuery] bool enabled = false)
    {
        var request = new UpdateFlagRequest()
        {
            FlagName = TenantConfigFlagNames.SendMonthlySecurityHealthReport.ToString(),
            Value = enabled.ToString()
        };

        await tenantConfigService.UpdateSystemFlagAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("force-send-monthly-security-health-report")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ForceMonthlyReportSend()
    {
        var job = jobManager.NewJob<SecurityHealthCheckJob>(tenantContext.DotYouRegistryId);
        job.Data = new SecurityHealthCheckJobData()
        {
            Tenant = WebOdinContext.Tenant,
            Force = true
        };

        await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromSeconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(0),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(0),
        });

        return Ok();
    }

    [HttpGet("monthly-security-health-report-status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<IActionResult> GetSecurityHealthReportStatus()
    {
        var settings = await tenantConfigService.GetTenantSettingsAsync();
        return Ok(settings.SendMonthlySecurityHealthReport);
    }

    [HttpPost("update-recovery-email")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<IActionResult> UpdateRecoveryEmail([FromBody] UpdateRecoveryEmailRequest request)
    {
        await securityHealthService.StartUpdateRecoveryEmail(request.Email, request.PasswordReply, WebOdinContext);
        return Ok();
    }

    [HttpGet("needs-attention")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<ActionResult<NeedsAttentionResponse>> RecoveryNeedsAttention()
    {
        var needsAttention = await securityHealthService.GetSecurityNeedsAttentionStatus(WebOdinContext);
        return Ok(new NeedsAttentionResponse() { NeedsAttention = needsAttention });
    }

    [HttpGet("recovery-risk-report")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> GetHealthCheck()
    {
        var check = await securityHealthService.RunHealthCheck(WebOdinContext);
        return Ok(check);
    }
}
