using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Newtonsoft.Json;
using Youverse.Core.Services.Mediator.ClientNotifications;
using Youverse.Core.Services.Notifications;

namespace Youverse.Core.Services.ClientNotifications
{
    public class AppNotificationHandler : WebSocketHandlerBase,
        INotificationHandler<NewInboxItemNotification>,
        INotificationHandler<IOwnerConsoleNotification>
    {
        public AppNotificationHandler(SocketConnectionManager webSocketConnectionManager) : base(webSocketConnectionManager)
        {
        }

        public override async Task OnConnected(WebSocket socket)
        {
            await base.OnConnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);
            await this.SerializeSendToAll(new ClientConnected() {SocketId = socketId});
        }

        public override async Task OnDisconnected(WebSocket socket)
        {
            await base.OnDisconnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);
            await this.SerializeSendToAll(new ClientDisconnected() {SocketId = socketId});
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
            var json = JsonConvert.SerializeObject(notification);
            await SendMessageToAllAsync(json);
        }
    }
}