#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstantsV1.NotificationsV1)]
    public class AppNotificationSocketController : OdinControllerBase
    {
        private readonly AppNotificationHandler _notificationHandler;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;


        public AppNotificationSocketController(
            AppNotificationHandler notificationHandler,
            IHostApplicationLifetime hostApplicationLifetime)
        {
            _notificationHandler = notificationHandler;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        /// <summary />
        [Route("ws")]
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
                var acceptContext = new WebSocketAcceptContext { DangerousEnableCompression = true };

                // Echo the app-level subprotocol so browsers accept the 101 when the client
                // authenticated via the "odin.bearer." subprotocol instead of the cookie.
                // Must match V2NotificationSocketController.
                if (HttpContext.WebSockets.WebSocketRequestedProtocols.Contains("odin.notify.v1"))
                {
                    acceptContext.SubProtocol = "odin.notify.v1";
                }

                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(acceptContext);

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

        [HttpPost("preauth")]
        public IActionResult SocketPreAuth()
        {
            //this only exists so we can use the [AuthorizeValidAppExchangeGrant] attribute to trigger the clienttokenauthhandler
            return Ok();
        }
    }
}