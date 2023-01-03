using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.ClientNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator.ClientNotifications;

namespace Youverse.Core.Services.AppNotifications
{
    public class EstablishConnectionRequest
    {
        public List<TargetDrive> Drives { get; set; }
    }

    public class AppNotificationHandler : WebSocketHandlerBase,
        INotificationHandler<NewInboxItemNotification>,
        INotificationHandler<IOwnerConsoleNotification>
    {
        private DotYouContextAccessor _contextAccessor;

        public AppNotificationHandler(SocketConnectionManager webSocketConnectionManager, DotYouContextAccessor contextAccessor) : base(webSocketConnectionManager)
        {
            _contextAccessor = contextAccessor;
        }

        public override async Task OnConnected(WebSocket socket, EstablishConnectionRequest request)
        {
            var dotYouContext = _contextAccessor.GetCurrent();
            await base.OnConnected(socket, request);

            var socketId = WebSocketConnectionManager.GetId(socket);
            await this.SerializeSendToAll(new ClientConnected() { SocketId = socketId });
        }

        public override async Task OnDisconnected(WebSocket socket)
        {
            await base.OnDisconnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);
            await this.SerializeSendToAll(new ClientDisconnected() { SocketId = socketId });
        }

        public override Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, byte[] buffer)
        {
            throw new System.NotImplementedException();
        }

        public async Task Handle(NewInboxItemNotification notification, CancellationToken cancellationToken)
        {
            await this.SerializeSendToAll(notification);
        }

        public async Task Handle(IOwnerConsoleNotification notification, CancellationToken cancellationToken)
        {
            await this.SerializeSendToAll(notification);
        }

        private async Task SerializeSendToAll(object notification)
        {
            var json = DotYouSystemSerializer.Serialize(notification);
            // await SendMessageAsync(json);
        }
    }
}