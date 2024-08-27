using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Background.Services.Tenant;

public sealed class MasterKeyAvailableBackgroundService(
    ILogger<MasterKeyAvailableBackgroundService> logger,
    IMultiTenantContainerAccessor tenantContainerAccessor,
    Odin.Services.Tenant.Tenant tenant)
    : AbstractBackgroundService(logger)
{
    private readonly Odin.Services.Tenant.Tenant _tenant = tenant;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scope = tenantContainerAccessor.Container().GetTenantScope(_tenant.Name);

        var accessor = scope.Resolve<MasterKeyContextAccessor>();
        var circleNetworkIntroductionService = scope.Resolve<CircleNetworkIntroductionService>();
        var tenantSystemStorage = scope.Resolve<TenantSystemStorage>();
        var tenantContext = scope.Resolve<TenantContext>();
        while (!stoppingToken.IsCancellationRequested)
        {
            var mkContext = (IOdinContext)accessor.GetContext();
            if (mkContext != null)
            {
                if(tenantContext.Settings.AutoAcceptIntroductions)
                {
                    using var cn = tenantSystemStorage.CreateConnection();
                    // await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequests(mkContext, cn);
                }
            }

            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}