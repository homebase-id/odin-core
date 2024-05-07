using System;
using MediatR;
using Odin.Services.AppNotifications;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Peer;

namespace Odin.Services.Mediator;

public class TransitFileReceivedNotification : MediatorNotificationBase
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.TransitFileReceived;

    public ExternalFileIdentifier TempFile { get; init; }
    public FileSystemType FileSystemType { get; init; }
    public TransferFileType TransferFileType { get; init; }
    public DatabaseConnection  DatabaseConnection { get; init; }
}