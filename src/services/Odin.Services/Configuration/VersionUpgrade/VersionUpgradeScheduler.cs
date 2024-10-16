using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.JobManagement;

namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class VersionUpgradeScheduler(
    TenantConfigService configService,
    VersionUpgradeService versionUpgradeService,
    ILogger<VersionUpgradeScheduler> logger,
    IJobManager jobManager)
{
    private readonly IJobManager _jobManager = jobManager;

    public async Task ScheduleUpgradeJobIfNeeded(IOdinContext odinContext)
    {
        if (!odinContext.Caller.HasMasterKey || !RequiresUpgrade())
        {
            return;
        }

        var job = _jobManager.NewJob<VersionUpgradeJob>();

        var json = OdinSystemSerializer.Serialize(odinContext);
        var (iv, encryptedContext) = AesCbc.Encrypt(json.ToUtf8ByteArray(), versionUpgradeService.TemporalEncryptionKey);
        job.Data = new VersionUpgradeJobData()
        {
            Iv = iv,
            Tenant = odinContext.Tenant,
            EncryptedOdinContextData = encryptedContext
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

    public bool RequiresUpgrade()
    {
        var currentVersion = configService.GetVersionInfo().DataVersionNumber;
        return currentVersion != ReleaseVersionInfo.DataVersionNumber;
    }
}