using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAccepted : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestAccepted;
        public bool IsEncrypted { get; set; } = false;

        public OdinId Sender { get; set; }
        public OdinId Recipient { get; set; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                Sender = this.Sender.DomainName,
                Recipient = this.Recipient.DomainName
            });
        }
    }
}