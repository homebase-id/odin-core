using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class VersionUpgradeService(
    ILogger<VersionUpgradeService> logger,
    IMultiTenantContainerAccessor tenantContainerAccessor,
    Odin.Services.Tenant.Tenant tenant,
    IForgottenTasks forgottenTasks)
{
    private IOdinContext _odinContext;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int WaitTimeSeconds = 60;
    private UnixTimeUtc LastRunTime { get; set; }

    private bool IsRunning { get; set; }

    public void TryRunNow(IOdinContext odinContext)
    {
        this._odinContext = odinContext;
        var scope = tenantContainerAccessor.Container().GetTenantScope(tenant.Name);
        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        using var cn = tenantSystemStorage.CreateConnection();

        if (ShouldRun(cn))
        {
            var t = this.ExecuteAsync(cn, CancellationToken.None);
            forgottenTasks.Add(t);
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