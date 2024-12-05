using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications;

public class NewFollowerNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.NewFollower;
    public Guid NotificationTypeId { get; } = Guid.Parse("2cc468af-109b-4216-8119-542401e32f4d");
    public OdinId Sender { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            Sender = this.Sender.DomainName
        });
    }
}