using MediatR;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public class ClientDisconnected : INotification, IOwnerConsoleNotification
    {
        public string Key => "ClientDisconnected";
        public string SocketId { get; set; }
    }
}