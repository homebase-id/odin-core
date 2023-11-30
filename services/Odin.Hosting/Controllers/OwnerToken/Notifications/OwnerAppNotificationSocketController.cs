#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.NotificationsV1)]
    public class OwnerAppNotificationSocketController : Controller
    {
        private readonly AppNotificationHandler _notificationHandler;

        public OwnerAppNotificationSocketController(AppNotificationHandler notificationHandler)
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
    }
}