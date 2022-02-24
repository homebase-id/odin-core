using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Notifications;

namespace Youverse.Hosting.Middleware
{
    public class NotificationWebSocketMiddleware
    {
        private readonly RequestDelegate _next;

        public NotificationWebSocketMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, AppNotificationHandler appNotificationHandler, DotYouContextAccessor dotYouContextAccessor)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            await appNotificationHandler.OnConnected(socket);

            await Receive(socket, async (result, buffer) =>
            {
                // if (result.MessageType == WebSocketMessageType.Text)
                // {
                //     await appNotificationHandler.ReceiveAsync(socket, result, buffer);
                //     return;
                // }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await appNotificationHandler.OnDisconnected(socket);
                    return;
                }

                throw new YouverseSecurityException("Incoming messages not allowed");
            });
        }

        private async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
        {
            var buffer = new byte[1024 * 4];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                handleMessage(result, buffer);
            }
        }
    }
}