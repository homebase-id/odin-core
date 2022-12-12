using MediatR;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public class ConnectionRequestAccepted : IOwnerConsoleNotification
    {
        public string Key => "ConnectionRequestAccepted";

        public DotYouIdentity Sender { get; set; }
    }
}