#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

/// <summary>
/// A connected identity's peer-notification token -- the one their own server fetched for the peer socket -- read with
/// straight from their device: Read on the zero-knowledge drives their connection reaches, nothing else
/// (<see cref="PeerAppNotificationService.GetDirectReadContextAsync"/>).  It lets a device download what is shared with
/// it from the sharer's server, as it hears of the changes there, without the relay through its own server.
/// </summary>
/// <remarks>
/// A token that is no live subscriber's fails, on any route: 401 means only that.  A live one counts only on an action
/// marked <see cref="PeerDirectReadAttribute"/>, for a drive it reads; anywhere else its caller is anonymous, so it gains
/// nothing there.  On a public drive the file reads skip the drive's grant, which is why the drive is asked here.
/// </remarks>
public class PeerSubscriberAuthPathHandler : IAuthPathHandler
{
    // The caller as if it brought no token (AuthHandlerResult.Fallback fails the request instead)
    private static readonly AuthHandlerResult Anonymous = new() { Status = AuthHandlerStatus.AnonymousFallback };

    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
    {
        var service = context.RequestServices.GetRequiredService<PeerAppNotificationService>();
        var ctx = await service.GetDirectReadContextAsync(clientAuthToken, odinContext);
        if (null == ctx)
        {
            return AuthHandlerResult.Fail();
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<PeerDirectReadAttribute>() == null ||
            !Guid.TryParse(context.GetRouteValue("driveId")?.ToString(), out var driveId) ||
            !ctx.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read))
        {
            return Anonymous;
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
