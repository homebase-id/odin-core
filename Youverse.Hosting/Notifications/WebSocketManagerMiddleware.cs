using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Youverse.Hosting.Notifications
{
    public class WebSocketManagerMiddleware
    {
        private WebSocketHandlerBase WebSocketHandlerBase { get; set; }
        private readonly RequestDelegate _next;

        public WebSocketManagerMiddleware(RequestDelegate next, WebSocketHandlerBase webSocketHandlerBase)
        {
            _next = next;
            WebSocketHandlerBase = webSocketHandlerBase;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            await WebSocketHandlerBase.OnConnected(socket);

            await Receive(socket, async (result, buffer) =>
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await WebSocketHandlerBase.ReceiveAsync(socket, result, buffer);
                    return;
                }

                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await WebSocketHandlerBase.OnDisconnected(socket);
                    return;
                }
            });
        }

        private async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
        {
            var buffer = new byte[1024 * 4];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer: new ArraySegment<byte>(buffer),
                    cancellationToken: CancellationToken.None);

                handleMessage(result, buffer);
            }
        }
    }
}