using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
{
    public class ConnectionRequestReceived : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionRequestReceived;

        public OdinId Sender { get; set; }
        public OdinId Recipient { get; set; }
        public Guid NotificationTypeId { get; } = Guid.Parse("8ee62e9e-c224-47ad-b663-21851207f768");

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