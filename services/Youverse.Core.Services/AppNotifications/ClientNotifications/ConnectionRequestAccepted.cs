using Youverse.Core.Identity;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAccepted : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestAccepted;

        public DotYouIdentity Sender { get; set; }
    }
}