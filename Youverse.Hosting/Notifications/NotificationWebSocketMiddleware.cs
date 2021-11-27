using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Notifications;

namespace Youverse.Hosting.Notifications
{
    public class NotificationWebSocketMiddleware
    {
        private readonly RequestDelegate _next;

        public NotificationWebSocketMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, NotificationHandler notificationHandler, DotYouContext dotYouContext)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            await notificationHandler.OnConnected(socket);

            await Receive(socket, async (result, buffer) =>
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await notificationHandler.ReceiveAsync(socket, result, buffer);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await notificationHandler.OnDisconnected(socket);
                    return;
                }
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