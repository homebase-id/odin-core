using System;
using MediatR;
using Odin.Services.AppNotifications;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer;

namespace Odin.Services.Mediator;

public class TransitFileReceivedNotification(OdinContext context) : MediatorNotificationBase(context)
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.TransitFileReceived;

    public ExternalFileIdentifier TempFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
    public TransferFileType TransferFileType { get; set; }
}