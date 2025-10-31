using System;
using MediatR;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Peer;

namespace Odin.Services.Mediator;

public class InboxItemReceivedNotification : EventArgs, INotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.InboxItemReceived;

    public TargetDrive TargetDrive { get; init; }
    public FileSystemType FileSystemType { get; init; }
    public TransferFileType TransferFileType { get; init; }

}