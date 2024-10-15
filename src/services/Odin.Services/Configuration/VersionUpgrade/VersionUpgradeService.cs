using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class VersionUpgradeService(
    ILogger<VersionUpgradeService> logger,
    IMultiTenantContainerAccessor tenantContainerAccessor,
    Odin.Services.Tenant.Tenant tenant,
    IJobManager jobManager)
{
    private IOdinContext _odinContext;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int WaitTimeSeconds = 60;
    private UnixTimeUtc LastRunTime { get; set; }

    private bool IsRunning { get; set; }

    public async Task TryRunNow(IOdinContext odinContext)
    {
        var scope = tenantContainerAccessor.Container().GetTenantScope(tenant.Name);
        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        using var cn = tenantSystemStorage.CreateConnection();

        if (ShouldRun(cn))
        {
            var job = jobManager.NewJob<VersionUpgradeJob>();

            //TODO save key in memory
            var key = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var json = OdinSystemSerializer.Serialize(odinContext);
            var (iv, encryptedContext) = AesCbc.Encrypt(json.ToUtf8ByteArray(), key);
            job.Data = new VersionUpgradeJobData()
            {
                Iv = iv,
                OdinContextData = encryptedContext.ToBase64()
            };

            await jobManager.ScheduleJobAsync(job, new JobSchedule
            {
                RunAt = DateTimeOffset.Now,
                MaxAttempts = 20,
                RetryDelay = TimeSpan.FromMinutes(1),
                OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
                OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
            });
        }
    }

    private async Task ExecuteAsync(DatabaseConnection cn, CancellationToken stoppingToken)
    {
        var scope = tenantContainerAccessor.Container().GetTenantScope(tenant.Name);
        var configService = scope.Resolve<TenantConfigService>();

        await _lock.WaitAsync(stoppingToken);
        IsRunning = true;
        LastRunTime = UnixTimeUtc.Now();

        var currentVersion = configService.GetVersionInfo(cn).DataVersionNumber;

        try
        {
            if (currentVersion == 0)
            {
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                // prepare introductions
                currentVersion = configService.IncrementVersion(cn).DataVersionNumber;
            }

            if (currentVersion == 1)
            {
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                // do something else
                _ = configService.IncrementVersion(cn).DataVersionNumber;
            }

            // ...
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
        finally
        {
            _lock.Release();
            IsRunning = false;
        }
    }

    private bool ShouldRun(DatabaseConnection cn)
    {
        if (UnixTimeUtc.Now() > LastRunTime.AddSeconds(WaitTimeSeconds))
        {
            return false;
        }

        if (IsRunning)
        {
            return false;
        }

        if (!_odinContext.Caller.HasMasterKey)
        {
            return false;
        }

        var scope = tenantContainerAccessor.Container().GetTenantScope(tenant.Name);
        var configService = scope.Resolve<TenantConfigService>();
        var currentVersion = configService.GetVersionInfo(cn).DataVersionNumber;
        if (currentVersion == ReleaseVersionInfo.DataVersionNumber)
        {
            return false;
        }

        return true;
    }

    public void BlockIfRunning()
    {
        if (IsRunning)
        {
            throw new VersionUpgradeRunningException("Version Upgrade in progress");
        }
    }
}