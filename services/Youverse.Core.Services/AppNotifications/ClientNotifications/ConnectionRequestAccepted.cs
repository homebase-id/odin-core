using Youverse.Core.Identity;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAccepted : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestAccepted;
        public bool IsEncrypted { get; set; } = false;

        public OdinId Sender { get; set; }
        public OdinId Recipient { get; set; }

        public string GetClientData()
        {
            return DotYouSystemSerializer.Serialize(new
            {
                Sender = this.Sender.DomainName,
                Recipient = this.Recipient.DomainName
            });
        }
    }
}