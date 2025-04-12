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
        if (!odinContext.Caller.HasMasterKey)
        {
            return;
        }

        if (string.IsNullOrEmpty(odinContext.Tenant.DomainName))
        {
            return;
        }
        
        var (upgradeRequired, _, _) = await RequiresUpgradeAsync();
        if (!upgradeRequired)
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

    public async Task<(bool requiresUpgrade, int tenantVersion, FailedUpgradeVersionInfo failureInfo)> RequiresUpgradeAsync()
    {
        var currentVersion = (await configService.GetVersionInfoAsync()).DataVersionNumber;
        var failure = await configService.GetVersionFailureInfoAsync();

        var isConfigured = await configService.IsIdentityServerConfiguredAsync();
        if (!isConfigured)
        {
            // no need to upgrade on unconfigured identity
            return (requiresUpgrade: false, currentVersion, failure);
        }

        bool upgradeRequired;
        var versionTooLow = currentVersion < Version.DataVersionNumber;
        if (failure == null)
        {
            upgradeRequired = versionTooLow;
        }
        else
        {
            upgradeRequired = versionTooLow && failure.BuildVersion != Version.VersionText;
        }

        // if (upgradeRequired)
        // {
        //     logger.LogDebug("Upgrade test indicated that upgrade is required.  It will be scheduled only when you are running as owner " +
        //                     "Current Version: v{cv}, release version: v{rv} " +
        //                     "(previously failed build version: {failure})",
        //         currentVersion,
        //         Version.DataVersionNumber,
        //         failure?.BuildVersion ?? "none");
        // }

        return (upgradeRequired, currentVersion, failure);
    }

    public static void SetRequiresUpgradeResponse(HttpContext context)
    {
        context.Response.Headers.Append(OdinHeaderNames.RequiresUpgrade, bool.TrueString);
        // context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
    }
}