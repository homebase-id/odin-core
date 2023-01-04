using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.AppNotifications
{
    public class AppNotificationHandler : WebSocketHandlerBase, INotificationHandler<IClientNotification>
    {
        private readonly DotYouContextAccessor _contextAccessor;

        public AppNotificationHandler(SocketConnectionManager webSocketConnectionManager,
            DotYouContextAccessor contextAccessor) : base(webSocketConnectionManager)
        {
            _contextAccessor = contextAccessor;
        }

        public override async Task OnConnected(WebSocket socket, EstablishConnectionRequest request)
        {
            // var dotYouContext = _contextAccessor.GetCurrent();
            await base.OnConnected(socket, request);

            var response = new EstablishConnectionResponse() { Success = true };
            await SendMessageAsync(socket, DotYouSystemSerializer.Serialize(response));
            await ListenForDisconnect(socket);
        }

        private async Task ListenForDisconnect(WebSocket socket)
        {
            var buffer = new byte[1024 * 4];
            var receiveResult = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                await this.OnDisconnected(socket);
                //TODO: need to send the right response but not quite sure what that is.
                // await socket.CloseAsync(receiveResult.CloseStatus.Value, "", CancellationToken.None);
            }
        }

        public override async Task OnDisconnected(WebSocket socket)
        {
            await base.OnDisconnected(socket);
        }

        public override Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, byte[] buffer)
        {
            throw new System.NotImplementedException();
        }

        public async Task Handle(IClientNotification notification, CancellationToken cancellationToken)
        {
            await this.SerializeSendToAll(notification);
        }

        private async Task SerializeSendToAll(object notification)
        {
            var json = DotYouSystemSerializer.Serialize(notification);

            var sockets = base.WebSocketConnectionManager.GetAll().Values;
            foreach (var socket in sockets)
            {
                await this.SendMessageAsync(socket, json);
            }
        }
    }
}