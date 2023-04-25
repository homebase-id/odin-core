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
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Util;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.NotificationsV1)]
    public class OwnerNotificationSocketController : Controller
    {
        private readonly AppNotificationHandler _notificationHandler;

        public OwnerNotificationSocketController(AppNotificationHandler notificationHandler)
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