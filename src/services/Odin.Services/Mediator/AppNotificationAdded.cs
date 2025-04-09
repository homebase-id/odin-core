using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Mediator;

public class AppNotificationAddedNotification(Guid typeId) : MediatorNotificationBase, IClientNotification
{
    public Guid Id { get; set; }
    public OdinId SenderId { get; set; }
    public UnixTimeUtc Timestamp { get; set; }
    public AppNotificationOptions AppNotificationOptions { get; set; }
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.AppNotificationAdded;
    public Guid NotificationTypeId { get; } = typeId;

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            Id,
            SenderId,
            Timestamp,
            AppNotificationOptions,
            NotificationType,
            NotificationTypeId
        });
    }
}