#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

/// <summary>
/// A connected identity's peer-notification token -- the one their own server fetched for the peer socket -- read with
/// straight from their device: Read on the zero-knowledge drives their connection reaches, nothing else
/// (<see cref="PeerAppNotificationService.GetDirectReadContextAsync"/>).  It lets a device download what is shared with
/// it from the sharer's server, as it hears of the changes there, without the relay through its own server.
/// </summary>
public class PeerSubscriberAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
    {
        // The drive routes only, whatever other route a policy lets any caller reach
        if (!(context.Request.Path.Value ?? "").StartsWith(UnifiedApiRouteConstants.DrivesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return AuthHandlerResult.Fail();
        }

        var service = context.RequestServices.GetRequiredService<PeerAppNotificationService>();
        var ctx = await service.GetDirectReadContextAsync(clientAuthToken, odinContext);
        if (null == ctx)
        {
            return AuthHandlerResult.Fail();
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);
        return AuthHandlerResult.Success();
    }

    public Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}
