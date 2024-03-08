using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAccepted : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestAccepted;
        public Guid NotificationTypeId { get; } = Guid.Parse("79f0932a-056e-490b-8208-3a820ad7c321");
        
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