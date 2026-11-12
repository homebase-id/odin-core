#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
public class PeerSubscriberAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
    {
        if (!TryGetDriveRead(context.Request, out var driveId))
        {
            return AuthHandlerResult.Fail();
        }

        var service = context.RequestServices.GetRequiredService<PeerAppNotificationService>();
        var ctx = await service.GetDirectReadContextAsync(clientAuthToken, odinContext);

        // The drive must be one the token reads: on a public drive the file reads skip the drive's grant
        if (null == ctx || !ctx.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read))
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

    /// <summary>
    /// A read of one drive: a GET under <c>/api/v2/drives/{driveId}/</c>, or that drive's query-batch.  Any other route
    /// a policy lets every caller reach is refused, as is any other POST under the drive: one may write
    /// (<c>expire-now</c>).
    /// </summary>
    private static bool TryGetDriveRead(HttpRequest request, out Guid driveId)
    {
        driveId = Guid.Empty;
        var path = request.Path.Value ?? "";
        var prefix = UnifiedApiRouteConstants.DrivesRoot + "/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = path[prefix.Length..];
        var end = rest.IndexOf('/');
        if (end < 0 || !Guid.TryParse(rest[..end], out driveId))
        {
            return false;
        }

        return HttpMethods.IsGet(request.Method) ||
               (HttpMethods.IsPost(request.Method) && path.EndsWith("/files/query-batch", StringComparison.OrdinalIgnoreCase));
    }
}
