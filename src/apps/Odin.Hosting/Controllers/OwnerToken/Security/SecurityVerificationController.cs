using System;
using System.Threading.Tasks;
using Bitcoin.BIP39;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Security;
using Odin.Services.Security.Health;
using Odin.Services.Security.Job;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

[ApiController]
[Route(OwnerApiPathConstants.SecurityRecoveryV1)]
[AuthorizeValidOwnerToken]
public class SecurityVerificationController(
    OwnerSecurityHealthService securityHealthService,
    TenantConfigService tenantConfigService,
    IJobManager jobManager) : OdinControllerBase
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

    [HttpPost("update-monthly-security-health-report-status")]
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
    public async Task<IActionResult> ForceMonthlyReportSend()
    {
        var job = jobManager.NewJob<SecurityHealthCheckJob>();
        job.Data = new SecurityHealthCheckJobData()
        {
            Tenant =  WebOdinContext.Tenant,
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
    public async Task<IActionResult> GetSecurityHealthReportStatus()
    {
        var settings = await tenantConfigService.GetTenantSettingsAsync();
        return Ok(settings.SendMonthlySecurityHealthReport);
    }

    [HttpPost("update-recovery-email")]
    public async Task<IActionResult> UpdateRecoveryEmail([FromBody] UpdateRecoveryEmailRequest request)
    {
        await securityHealthService.StartUpdateRecoveryEmail(request.Email, request.PasswordReply, WebOdinContext);
        return Ok();
    }

    [HttpGet("needs-attention")]
    public async Task<ActionResult<NeedsAttentionResponse>> RecoveryNeedsAttention()
    {
        var needsAttention = await securityHealthService.GetSecurityNeedsAttentionStatus(WebOdinContext);
        return Ok(new NeedsAttentionResponse() { NeedsAttention = needsAttention });
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

public class NeedsAttentionResponse
{
    public bool NeedsAttention { get; set; }
}