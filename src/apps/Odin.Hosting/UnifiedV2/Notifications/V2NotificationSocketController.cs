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
    /// Cookie-less WebSocket entry point for clients (e.g. WASM, native) that cannot send
    /// the app/owner auth cookie on the upgrade. The client must authenticate by sending a
    /// SocketAuthenticationPackage as the first WS frame; AppNotificationHandler resolves
    /// the embedded ClientAuthToken64 against AppRegistrationService or
    /// PeerAppNotificationService based on its ClientTokenType.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route(UnifiedApiRouteConstants.NotifySocket)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2NotificationSocketController : OdinControllerBase
    {
        private readonly AppNotificationHandler _notificationHandler;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public V2NotificationSocketController(
            AppNotificationHandler notificationHandler,
            IHostApplicationLifetime hostApplicationLifetime)
        {
            _notificationHandler = notificationHandler;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        [HttpGet]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task Connect()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var cancellationTokenSources = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted,
                _hostApplicationLifetime.ApplicationStopping);

            try
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true
                });

                await _notificationHandler.EstablishConnection(webSocket, WebOdinContext, cancellationTokenSources.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (InvalidOperationException)
            {
                // this can happen when we need to upgrade
                //System.InvalidOperationException: The response status code for a Extended CONNECT request must be 2XX.
                VersionUpgradeScheduler.SetRequiresUpgradeResponse(HttpContext);
            }
        }
    }
}
