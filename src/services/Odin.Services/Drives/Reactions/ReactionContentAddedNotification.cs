using System;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Mediator;

namespace Odin.Services.Drives.Reactions;

public class ReactionContentAddedNotification : MediatorNotificationBase, IClientNotification
{
    public Reaction Reaction { get; init; }

    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ReactionContentAdded;

    public Guid NotificationTypeId { get; } = Guid.Parse("37dae95d-e137-4bd4-b782-8512aaa2c96a");

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(this.Reaction);
    }
}