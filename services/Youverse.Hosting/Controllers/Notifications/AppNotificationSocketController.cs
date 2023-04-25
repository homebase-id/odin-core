#nullable enable
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Controllers.Notifications
{
    [ApiController]
    [AuthorizeValidAppExchangeGrant]
    [Route(AppApiPathConstants.NotificationsV1)]
    public class AppNotificationSocketController : Controller
    {
        private readonly AppNotificationHandler _notificationHandler;

        public AppNotificationSocketController(AppNotificationHandler notificationHandler)
        {
            _notificationHandler = notificationHandler;
        }

        /// <summary />
        [HttpGet("ws")]
        public async Task Connect()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                await _notificationHandler.EstablishConnection(webSocket);
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }
    }
}