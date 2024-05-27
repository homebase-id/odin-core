using System;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class OutboxFileItemDeliverySuccessNotification : MediatorNotificationBase
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxFileItemDeliverySuccess;

    public OdinId Recipient { get; set; }
    public InternalDriveFileId File { get; set; }

    /// <summary>
    /// The version tag of the File that was processed
    /// </summary>
    public Guid VersionTag { get; set; }

    public FileSystemType FileSystemType { get; set; }
    public LatestTransferStatus TransferStatus { get; set; }
}