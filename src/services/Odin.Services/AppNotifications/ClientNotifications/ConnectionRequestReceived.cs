using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestReceived;

        public OdinId Sender { get; init; }
        public OdinId Recipient { get; init; }
        public Guid NotificationTypeId { get; } = Guid.Parse("8ee62e9e-c224-47ad-b663-21851207f768");
        public IdentityDatabase db { get; init; }

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