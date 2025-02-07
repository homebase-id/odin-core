using System;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.Drives.Reactions;

public class AllReactionsByFileDeleted : MediatorNotificationBase, IClientNotification
{
    public InternalDriveFileId FileId { get; init; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.AllReactionsByFileDeleted;
    
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.FileId);
    }
}