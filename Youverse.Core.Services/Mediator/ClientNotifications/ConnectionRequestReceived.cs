using MediatR;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public class ConnectionRequestReceived : INotification, IOwnerConsoleNotification
    {
        public string Key => "ConnectionRequestReceived";

        public DotYouIdentity Sender { get; set; }
    }
}