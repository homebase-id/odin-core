using System;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Core.Services.Drives.Reactions;

public class ReactionContentAddedNotification : EventArgs, IClientNotification
{
    public Reaction Reaction { get; set; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ReactionContentAdded;

    public Guid NotificationTypeId { get; } = Guid.Parse("37dae95d-e137-4bd4-b782-8512aaa2c96a");


    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.Reaction);
    }
}

public class ReactionDeletedNotification : EventArgs, IClientNotification
{
    public Reaction Reaction { get; set; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ReactionContentDeleted;
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.Reaction);
    }
}

public class AllReactionsByFileDeleted : EventArgs, IClientNotification
{

    public InternalDriveFileId FileId { get; set; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.AllReactionsByFileDeleted;
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.FileId);
    }
}
