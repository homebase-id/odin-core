using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class OutboxFileItemDeliveryFailedNotification : MediatorNotificationBase
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxFileItemDeliveryFailed;

    public OdinId Recipient { get; set; }

    public InternalDriveFileId File { get; set; }

    public FileSystemType FileSystemType { get; set; }

    public LatestTransferStatus TransferStatus { get; set; }
}