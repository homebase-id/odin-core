using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.Connections.IcrKeyAvailableWorker;

public class IcrKeyAvailableBackgroundService(
    CircleNetworkIntroductionService circleNetworkIntroductionService,
    TenantContext tenantContext,
    CircleNetworkService circleNetworkService,
    ILogger<IcrKeyAvailableBackgroundService> logger)
{
    public int RunCount { get; set; }


    private async Task RunInternal(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            if (!tenantContext.Settings.DisableAutoAcceptIntroductions &&
                odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
            {
                await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequestsAsync(odinContext, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
    }
}