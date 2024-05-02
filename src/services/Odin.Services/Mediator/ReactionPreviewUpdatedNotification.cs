using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class ReactionPreviewUpdatedNotification : MediatorNotificationBase, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.StatisticsChanged;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;

    public InternalDriveFileId File { get; init; }

    public ServerFileHeader ServerFileHeader { get; init; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}