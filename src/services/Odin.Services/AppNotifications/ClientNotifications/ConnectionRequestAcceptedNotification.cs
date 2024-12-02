using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestAcceptedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestAccepted;
        public Guid NotificationTypeId { get; } = Guid.Parse("79f0932a-056e-490b-8208-3a820ad7c321");
        public OdinId Sender { get; init; }
        public OdinId Recipient { get; init; }

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