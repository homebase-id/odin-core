using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //TODO: need to throttle this

        var scope = tenantContainerAccessor.Container().GetTenantScope(_tenant.Name);

        var accessor = scope.Resolve<IcrKeyAvailableContext>();
        var circleNetworkIntroductionService = scope.Resolve<CircleNetworkIntroductionService>();
        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        var tenantContext = scope.Resolve<TenantContext>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var odinContext = (IOdinContext)accessor.GetContext();
                if (odinContext != null)
                {
                    if (!tenantContext.Settings.DisableAutoAcceptIntroductions &&
                        odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
                    {
                        using var cn = tenantSystemStorage.CreateConnection();
                        await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequests(odinContext, cn);
                    }

                    if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
                    {
                        using var sendRequestCn = tenantSystemStorage.CreateConnection();
                        await circleNetworkIntroductionService.SendOutstandingConnectionRequests(odinContext, sendRequestCn);
                    }

                    if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnections))
                    {
                        using var fixIcrCn = tenantSystemStorage.CreateConnection();
                        await circleNetworkService.UpgradeWeakClientAccessTokens(odinContext, fixIcrCn);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception occured");
            }

            await SleepAsync(TimeSpan.FromSeconds(100), stoppingToken);
        }
    }
}