using System;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Peer;

namespace Odin.Services.Mediator;

public class InboxItemReceivedNotification : MediatorNotificationBase
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.InboxItemReceived;

    public TargetDrive TargetDrive { get; init; }
    public FileSystemType FileSystemType { get; init; }
    public TransferFileType TransferFileType { get; init; }

}