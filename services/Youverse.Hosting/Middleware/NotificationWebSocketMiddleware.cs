using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Middleware
{
    public class NotificationWebSocketMiddleware
    {
        private readonly RequestDelegate _next;

        public NotificationWebSocketMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return;
            }

            
            AppNotificationHandler appNotificationHandler = context.RequestServices.GetService<AppNotificationHandler>();
            // context.RequestServices.GetAutofacRoot()
            // var dotYouContext = dotYouContextAccessor.GetCurrent();
            
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            // await appNotificationHandler.OnConnected(socket);

            // await Receive(socket, async (result, buffer) =>
            // {
            //     // if (result.MessageType == WebSocketMessageType.Text)
            //     // {
            //     //     await appNotificationHandler.ReceiveAsync(socket, result, buffer);
            //     //     return;
            //     // }
            //
            //     if (result.MessageType == WebSocketMessageType.Close)
            //     {
            //         await appNotificationHandler.OnDisconnected(socket);
            //         return;
            //     }
            //
            //     throw new YouverseSecurityException("Incoming messages not allowed");
            // });
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