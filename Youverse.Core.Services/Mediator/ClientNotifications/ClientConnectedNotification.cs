using MediatR;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public class ClientConnected : IOwnerConsoleNotification
    {
        public string Key => "ClientConnected";

        public string SocketId { get; set; }
    }
}