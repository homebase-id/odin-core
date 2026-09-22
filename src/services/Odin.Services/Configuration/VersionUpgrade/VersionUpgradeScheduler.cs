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
    OdinConfiguration config,
    TenantConfigService configService,
    TenantContext tenantContext,
    ILogger<VersionUpgradeScheduler> logger,
    IJobManager jobManager,
    VersionUpgradeRunState runState)
{
    private readonly IJobManager _jobManager = jobManager;

    public async Task EnsureScheduledAsync(
        ClientAuthenticationToken token,
        IOdinContext odinContext,
        bool force = false)
    {
        if (!odinContext.Caller.HasMasterKey)
        {
            logger.LogDebug("VersionUpgradeScheduler -> Caller does not have master key");
            return;
        }

        if (string.IsNullOrEmpty(odinContext.Tenant.DomainName))
        {
            logger.LogDebug("VersionUpgradeScheduler -> No tenant domain name");
            return;
        }

        var (upgradeRequired, currentVersion, failureInfo) = await RequiresUpgradeAsync();

        if (force)
        {
            logger.LogDebug("VersionUpgradeScheduler -> Tenant is on v{cv}.  Forcing version upgrade", currentVersion);
        }
        else 
        {
            if (!upgradeRequired)
            {
                var failureText = "";
                if (failureInfo != null)
                {
                    failureText = $"(Failure Info: {failureInfo.BuildVersion} " +
                                  $"Last Attempt: {failureInfo.LastAttempted} " +
                                  $"Failed version: {failureInfo.FailedDataVersionNumber})";
                }

                logger.LogDebug("VersionUpgradeScheduler -> Tenant is on v{cv}.  Upgrade not required {fi}", currentVersion, failureText);
                return;
            }
        }

        var job = _jobManager.NewJob<VersionUpgradeJob>();

        var (iv, encryptedToken) = AesCbc.Encrypt(token.ToPortableBytes(), tenantContext.TemporalEncryptionKey);

        job.Data = new VersionUpgradeJobData()
        {
            Iv = iv,
            Tenant = odinContext.Tenant,
            EncryptedToken = encryptedToken,
            TestMode = config.Development.VersionUpgradeTestModeEnabled
        };

        logger.LogInformation("Scheduling version upgrade job. Tenant current version: {cv}.", currentVersion);
        await _jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromSeconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(0),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(0),
        });
    }

    /// <summary>
    /// What the upgrade is doing, as one value. See <see cref="UpgradeState"/> for why the signals
    /// this folds together are a trap to hand out separately.
    /// </summary>
    public async Task<UpgradeState> GetUpgradeStateAsync()
    {
        var (requiresUpgrade, tenantVersion, failureInfo) = await RequiresUpgradeAsync();
        return GetUpgradeState(requiresUpgrade, tenantVersion, failureInfo);
    }

    /// <summary>
    /// <see cref="GetUpgradeStateAsync"/> for a caller that has already asked
    /// <see cref="RequiresUpgradeAsync"/> -- the version-info endpoint is polled every few seconds
    /// while an upgrade runs, and asking twice per request to answer one question is a waste.
    /// </summary>
    public UpgradeState GetUpgradeState(
        bool requiresUpgrade,
        int tenantVersion,
        FailedUpgradeVersionInfo failureInfo)
    {
        if (runState.IsRunning)
        {
            return UpgradeState.Running;
        }

        if (requiresUpgrade)
        {
            return failureInfo == null ? UpgradeState.Pending : UpgradeState.Failed;
        }

        // RequiresUpgradeAsync answers "should this be attempted now", so it also says no when the
        // last attempt failed on the build that is running: retrying it would fail the same way, and
        // the wait is for a new build rather than for a job. The data is still behind, though, and
        // reporting that as up to date is how a client ends up waiting for something that is over.
        if (failureInfo != null && tenantVersion < Version.DataVersionNumber)
        {
            return UpgradeState.Failed;
        }

        return UpgradeState.UpToDate;
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

        return (upgradeRequired, currentVersion, failure);
    }

    public static void SetRequiresUpgradeResponse(HttpContext context)
    {
        context.Response.Headers.Append(OdinHeaderNames.RequiresUpgrade, bool.TrueString);
        // context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
    }
}