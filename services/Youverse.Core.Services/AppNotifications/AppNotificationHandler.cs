using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.AppNotifications
{
    public class AppNotificationHandler : WebSocketHandlerBase,
        INotificationHandler<IOwnerConsoleNotification>,
        INotificationHandler<DriveFileChangedNotification>,
        INotificationHandler<DriveFileDeletedNotification>
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
        }

        public override async Task OnDisconnected(WebSocket socket)
        {
            await base.OnDisconnected(socket);
        }

        public override Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, byte[] buffer)
        {
            throw new System.NotImplementedException();
        }

        public async Task Handle(IOwnerConsoleNotification notification, CancellationToken cancellationToken)
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

        public async Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken)
        {
            var clientNotification = new ClientNotification()
            {
                NotificationType = ClientNotificationType.FileAdded,
                Data = DotYouSystemSerializer.Serialize(notification)
            };
            
            await this.SerializeSendToAll(clientNotification);
        }

        public async Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            await this.SerializeSendToAll(notification);
        }
    }
}