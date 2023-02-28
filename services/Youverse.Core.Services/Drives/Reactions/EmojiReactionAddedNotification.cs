using System;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

public class EmojiReactionAddedNotification : EventArgs, IClientNotification
{
    public Reaction Reaction { get; set; }
    
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.EmojiReactionAdded;

    public string GetClientData()
    {
        return DotYouSystemSerializer.Serialize(this.Reaction);
    }
}