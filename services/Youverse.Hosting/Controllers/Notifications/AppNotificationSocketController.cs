#nullable enable
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Security;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.ClientToken;
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
            
            
            // var success = ClientAuthenticationToken.TryParse(request.Cat64, out var clientAuthToken);
            //
            // if (!success)
            // {
            //     throw new YouverseClientException("bad token");
            // }
            //
            // var options = new CookieOptions()
            // {
            //     HttpOnly = true,
            //     IsEssential = true,
            //     Secure = true,
            //     //Path = "/owner", //TODO: cannot use this until we adjust api paths
            //     // SameSite = SameSiteMode.Strict,
            //     Expires = DateTime.UtcNow.AddMonths(6)
            // };
            //
            // Response.Cookies.Append(ClientTokenConstants.ClientAuthTokenCookieName, clientAuthToken.ToString(), options);

            return Ok();
        }
        
    }

    public class SocketPreAuthRequest
    {
        public string? Cat64 { get; set; }
    }
}