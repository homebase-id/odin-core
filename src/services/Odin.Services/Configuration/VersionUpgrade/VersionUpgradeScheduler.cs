using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.JobManagement;

namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class VersionUpgradeScheduler(
    TenantConfigService configService,
    TenantContext tenantContext,
    ILogger<VersionUpgradeScheduler> logger,
    IJobManager jobManager)
{
    private readonly IJobManager _jobManager = jobManager;

    public async Task EnsureScheduledAsync(
        ClientAuthenticationToken token,
        IOdinContext odinContext)
    {
        if (!odinContext.Caller.HasMasterKey || await RequiresUpgradeAsync() == false)
        {
            return;
        }

        if (string.IsNullOrEmpty(odinContext.Tenant.DomainName))
        {
            return;
        }

        var job = _jobManager.NewJob<VersionUpgradeJob>();

        var (iv, encryptedToken) = AesCbc.Encrypt(token.ToPortableBytes(), tenantContext.TemporalEncryptionKey);

        job.Data = new VersionUpgradeJobData()
        {
            Iv = iv,
            Tenant = odinContext.Tenant,
            EncryptedToken = encryptedToken
        };

        logger.LogInformation("Scheduling version upgrade job");
        await _jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromSeconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(0),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(0),
        });
    }

    public async Task<bool> RequiresUpgradeAsync()
    {
        var currentVersion = (await configService.GetVersionInfoAsync()).DataVersionNumber;
        
        logger.LogInformation("Checking Requires Upgrade.  current Version: {cv}, release version: {rv}",
            currentVersion, ReleaseVersionInfo.DataVersionNumber);
        
        return currentVersion != ReleaseVersionInfo.DataVersionNumber;
    }

    public static void SetRequiresUpgradeResponse(HttpContext context)
    {
        context.Response.Headers.Append(OdinHeaderNames.RequiresUpgrade, bool.TrueString);
        // context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
    }
}