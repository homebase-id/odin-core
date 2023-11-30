#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstants.NotificationsV1)]
    public class AppNotificationSocketController : Controller
    {
        private readonly AppNotificationHandler _notificationHandler;

        public AppNotificationSocketController(AppNotificationHandler notificationHandler)
        {
            _notificationHandler = notificationHandler;
        }

        /// <summary />
        [Route("ws")]
        public async Task Connect()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true
                });
                await _notificationHandler.EstablishConnection(webSocket);
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
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