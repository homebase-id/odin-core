using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Background.Services.Tenant;

/// <summary>
/// Used to process items when the ICR key is available from an app or owner console
/// </summary>
public sealed class IcrKeyAvailableBackgroundService(
    ILogger<IcrKeyAvailableBackgroundService> logger,
    IMultiTenantContainerAccessor tenantContainerAccessor,
    CircleNetworkService circleNetworkService,
    Odin.Services.Tenant.Tenant tenant)
    : AbstractBackgroundService(logger)
{
    private readonly Odin.Services.Tenant.Tenant _tenant = tenant;

    private IOdinContext _odinContext;
    private UnixTimeUtc _lastRun = UnixTimeUtc.ZeroTime;
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
        const int waitTimeSeconds = 10; //TODO: config

        while (!stoppingToken.IsCancellationRequested)
        {
            if (UnixTimeUtc.Now() > _lastRun.AddSeconds(waitTimeSeconds))
            {
                await Run(stoppingToken);
                _lastRun = UnixTimeUtc.Now();
            }
        
            await SleepAsync(TimeSpan.FromSeconds(100), stoppingToken);
        }
    }

    private async Task Run(CancellationToken stoppingToken)
    {
        var scope = tenantContainerAccessor.Container().GetTenantScope(_tenant.Name);
        var circleNetworkIntroductionService = scope.Resolve<CircleNetworkIntroductionService>();
        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        var tenantContext = scope.Resolve<TenantContext>();
        
        await _lock.WaitAsync(stoppingToken);

        try
        {
            var odinContext = this._odinContext;
            if (odinContext != null)
            {
                if (!tenantContext.Settings.DisableAutoAcceptIntroductions &&
                    odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
                {
                    var db = tenantSystemStorage.IdentityDatabase;
                    await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequests(odinContext, db);
                }

                if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
                {
                    var db = tenantSystemStorage.IdentityDatabase;
                    await circleNetworkIntroductionService.SendOutstandingConnectionRequests(odinContext, db);
                }

                if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnections))
                {
                    var db = tenantSystemStorage.IdentityDatabase;
                    await circleNetworkService.UpgradeWeakClientAccessTokens(odinContext);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
        finally
        {
            _lock.Release();
        }
    }
}