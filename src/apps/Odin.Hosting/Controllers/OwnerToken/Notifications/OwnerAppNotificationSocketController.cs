#nullable enable

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.NotificationsV1)]
    public class OwnerAppNotificationSocketController : OdinControllerBase
    {
        private readonly AppNotificationHandler _notificationHandler;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public OwnerAppNotificationSocketController(
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
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true
                });

                await _notificationHandler.EstablishConnection(webSocket, cancellationTokenSources.Token, WebOdinContext);
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