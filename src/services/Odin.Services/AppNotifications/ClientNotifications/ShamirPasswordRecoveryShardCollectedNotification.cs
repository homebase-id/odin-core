using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class ShamirPasswordRecoveryShardCollectedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.Unused;
        public Guid NotificationTypeId { get; } = Guid.Parse("e1cb2e75-2002-4ce0-a2e3-f228579229ef");

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