using System;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Mediator;

namespace Odin.Services.Drives.Reactions;

public class ReactionContentDeletedNotification : MediatorNotificationBase, IClientNotification
{
    public Reaction Reaction { get; set; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ReactionContentDeleted;

    public Guid NotificationTypeId { get; }

    public IdentityDatabase db { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.Reaction);
    }
}