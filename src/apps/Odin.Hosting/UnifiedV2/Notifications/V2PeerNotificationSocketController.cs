#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.UnifiedV2.Notifications
{
    /// <summary>
    /// WebSocket entry point for live notifications on a drive hosted by another (peer) identity —
    /// the V2 equivalent of <c>WS /api/apps/v1/notify/peer/ws</c>. The upgrade itself is anonymous;
    /// the client authenticates in-band by sending a <c>SocketAuthenticationPackage</c> carrying its
    /// peer-notification-subscriber token as the first message, which
    /// <see cref="PeerAppNotificationHandler"/> validates. Two routes are exposed so both native
    /// HTTP/1.1 clients and WASM/HTTP-2 Extended-CONNECT clients can upgrade, mirroring
    /// <see cref="V2NotificationSocketController"/>.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerNotificationSocketController(
        PeerAppNotificationHandler notificationHandler,
        IHostApplicationLifetime hostApplicationLifetime)
        : OdinControllerBase
    {
        [HttpGet]
        [Route(UnifiedApiRouteConstants.PeerNotifySocket)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task Connect()
        {
            return HandleAsync();
        }

        // Browser/WASM clients upgrade over HTTP/2 Extended CONNECT (RFC 8441), which MVC routing
        // rejects with 405 when only [HttpGet] is present; the wasm-facing route accepts both verbs.
        [AcceptVerbs("GET", "CONNECT")]
        [Route(UnifiedApiRouteConstants.PeerNotifySocketWasm)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task ConnectWasm()
        {
            return HandleAsync();
        }

        private async Task HandleAsync()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var cancellationTokenSources = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted,
                hostApplicationLifetime.ApplicationStopping);

            try
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true
                });

                // Authentication happens in-band: the handler treats the first message as a
                // SocketAuthenticationPackage and resolves the peer-subscriber context from it.
                await notificationHandler.EstablishConnection(webSocket, WebOdinContext, cancellationTokenSources.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (InvalidOperationException)
            {
                // Extended CONNECT requires a 2XX status; signal the client an upgrade is required.
                VersionUpgradeScheduler.SetRequiresUpgradeResponse(HttpContext);
            }
        }
    }
}
