using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Background.Services.Tenant;

public class VersionUpgradeContext
{
    public bool IsRunning { get; private set; }

    public void SetIsRunning(bool running)
    {
        this.IsRunning = running;
    }
}

/// <summary>
/// Used to handle upgrades to data for each tenant based on the version number
/// </summary>
public sealed class VersionUpgradeBackgroundService(
    ILogger<VersionUpgradeBackgroundService> logger,
    IMultiTenantContainerAccessor tenantContainerAccessor,
    Odin.Services.Tenant.Tenant tenant)
    : AbstractBackgroundService(logger)
{
    private readonly Odin.Services.Tenant.Tenant _tenant = tenant;

    private IOdinContext _odinContext;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void RunNow(IOdinContext odinContext)
    {
        this._odinContext = odinContext;

        //todo: need to check if this odincontext is worth running the process
        //i can do this by checking permissions, etc.

        this.PulseBackgroundProcessor();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //TODO: need to throttle this

        var scope = tenantContainerAccessor.Container().GetTenantScope(_tenant.Name);

        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        var tenantContext = scope.Resolve<TenantContext>();
        var versionContext = scope.Resolve<VersionUpgradeContext>();

        await _lock.WaitAsync(stoppingToken);
        versionContext.SetIsRunning(true);
        try
        {
            // dataConversion.PrepareIntroductionsRelease()
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
        finally
        {
            _lock.Release();
            versionContext.SetIsRunning(false);
        }

        await SleepAsync(TimeSpan.FromSeconds(100), stoppingToken);
    }
}