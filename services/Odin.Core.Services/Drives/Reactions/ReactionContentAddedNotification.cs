using System;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Core.Services.Drives.Reactions;

public class ReactionContentAddedNotification : EventArgs, IClientNotification
{
    public Reaction Reaction { get; set; }
    
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ReactionContentAdded;

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.Reaction);
    }
}