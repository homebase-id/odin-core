using Youverse.Core.Identity;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : IOwnerConsoleNotification
    {
        public string Key => "ConnectionRequestReceived";

        public DotYouIdentity Sender { get; set; }
    }
}