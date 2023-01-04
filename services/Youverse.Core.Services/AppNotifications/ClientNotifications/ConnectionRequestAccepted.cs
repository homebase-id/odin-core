using Youverse.Core.Identity;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAccepted : IOwnerConsoleNotification
    {
        public string Key => "ConnectionRequestAccepted";

        public DotYouIdentity Sender { get; set; }
    }
}