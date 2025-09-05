using System;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class ShamirPasswordRecoverySufficientShardsCollectedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.Unused;

        // public OdinId Sender { get; init; }
        // public OdinId Recipient { get; init; }
        public Guid NotificationTypeId { get; } = Guid.Parse("0df41b47-939e-47c0-8439-d38ce8b4d048");

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                // Sender = this.Sender.DomainName,
                // Recipient = this.Recipient.DomainName
            });
        }
    }
}