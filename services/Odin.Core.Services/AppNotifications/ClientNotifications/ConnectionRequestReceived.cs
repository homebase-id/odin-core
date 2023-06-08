using Odin.Core.Identity;
using Odin.Core.Serialization;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestReceived;

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