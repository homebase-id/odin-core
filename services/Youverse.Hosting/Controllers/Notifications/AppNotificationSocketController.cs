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
    [AuthorizeValidAppExchangeGrant]
    [Route(AppApiPathConstants.NotificationsV1)]
    public class AppNotificationSocketController : Controller
    {
        private readonly string _currentTenant;
        private AppNotificationHandler _notificationHandler;

        public AppNotificationSocketController(ITenantProvider tenantProvider, AppNotificationHandler notificationHandler)
        {
            _notificationHandler = notificationHandler;
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
        }

        [HttpGet("ws")]
        public async Task Connect()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

                var request = await ReceiveConfiguration(webSocket);
                if (null != request)
                {
                    await _notificationHandler.Connect(webSocket, request);
                }
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        private async Task<EstablishConnectionRequest?> ReceiveConfiguration(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            EstablishConnectionRequest? request = null;
            if (receiveResult.MessageType == WebSocketMessageType.Text) //must be JSON
            {
                Array.Resize(ref buffer, receiveResult.Count);
                request = await DotYouSystemSerializer.Deserialize<EstablishConnectionRequest>(buffer.ToMemoryStream());
            }

            if (null == request)
            {
                //send a close method
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, 0),
                    WebSocketMessageType.Close,
                    WebSocketMessageFlags.EndOfMessage,
                    CancellationToken.None);
            }

            return request;
        }

        private static async Task Echo(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!receiveResult.CloseStatus.HasValue)
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                    receiveResult.MessageType,
                    receiveResult.EndOfMessage,
                    CancellationToken.None);

                receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }

            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }
    }
}