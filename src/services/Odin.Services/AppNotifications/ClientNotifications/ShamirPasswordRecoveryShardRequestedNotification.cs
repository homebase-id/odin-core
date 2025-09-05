using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    /// <summary>
    /// Sent to a player when a dealer has requested their shard
    /// </summary>
    public class ShamirPasswordRecoveryShardRequestedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.Unused;
        public Guid NotificationTypeId { get; } = Guid.Parse("260e370d-85d5-4ed9-92ed-bb2b36b0f73c");

        public OdinId Sender { get; init; }
        public string AdditionalMessage { get; init; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                Sender = this.Sender.DomainName,
                message = this.AdditionalMessage
            });
        }
    }
}