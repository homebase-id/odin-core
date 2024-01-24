#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstants.NotificationsV1)]
    public class AppNotificationSocketController : Controller
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

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
            {
                DangerousEnableCompression = true
            });

            try
            {
                await _notificationHandler.EstablishConnection(webSocket, cancellationTokenSources.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        [HttpPost("preauth")]
        public IActionResult SocketPreAuth()
        {
            //this only exists so we can use the [AuthorizeValidAppExchangeGrant] attribute to trigger the clienttokenauthhandler
            return Ok();
        }
        
    }

    public class SocketPreAuthRequest
    {
        public string? Cat64 { get; set; }
    }
}