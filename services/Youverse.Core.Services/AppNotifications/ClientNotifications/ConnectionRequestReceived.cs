using Youverse.Core.Identity;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestReceived;
        
        public DotYouIdentity Sender { get; set; }
    }
}