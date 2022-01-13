using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Newtonsoft.Json;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Notifications
{
    public class AppNotificationHandler : WebSocketHandlerBase, INotificationHandler<NewInboxItemNotification>
    {
        public AppNotificationHandler(SocketConnectionManager webSocketConnectionManager) : base(webSocketConnectionManager)
        {
        }

        public override async Task OnConnected(WebSocket socket)
        {
            await base.OnConnected(socket);

            var socketId = WebSocketConnectionManager.GetId(socket);
            await SendMessageToAllAsync($"{socketId} is now connected");
        }

        public override async Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, byte[] buffer)
        {
            var socketId = WebSocketConnectionManager.GetId(socket);
            var message = $"{socketId} said: {Encoding.UTF8.GetString(buffer, 0, result.Count)}";

            await SendMessageToAllAsync(message);
        }
        
        public override Task OnDisconnected(WebSocket socket)
        {
            return base.OnDisconnected(socket);
        }
        
        public async Task Handle(NewInboxItemNotification notification, CancellationToken cancellationToken)
        {
            //TODO: add some standard fields to this notification
            var json = JsonConvert.SerializeObject(notification);
            await SendMessageToAllAsync(json);
        }
    }
}