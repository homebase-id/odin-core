using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication;
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
            var odinContext = (IOdinContext)accessor.GetContext();
            if (odinContext != null)
            {
                if (tenantContext.Settings.AutoAcceptIntroductions)
                {
                    using var cn = tenantSystemStorage.CreateConnection();
                    await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequests(odinContext, cn);
                }
                
                using var sendRequestCn = tenantSystemStorage.CreateConnection();
                await circleNetworkIntroductionService.SendOutstandingConnectionRequests(odinContext, sendRequestCn);

                using var fixIcrCn = tenantSystemStorage.CreateConnection();
                await circleNetworkService.UpgradeClientAccessTokens(odinContext, fixIcrCn);
            }

            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}