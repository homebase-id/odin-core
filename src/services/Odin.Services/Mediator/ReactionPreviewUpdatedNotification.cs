using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class ReactionPreviewUpdatedNotification : MediatorNotificationBase, IDriveNotification
{
    // A reaction/comment summary change is surfaced to clients as a fileModified (the file's header
    // changed). Previously this was StatisticsChanged; clients now handle a single fileModified for
    // reactions, comments, and peer reactions. StatisticsChanged is no longer emitted on the wire.
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;

    public InternalDriveFileId File { get; init; }

    public ServerFileHeader ServerFileHeader { get; init; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

    public bool IgnoreFeedDistribution { get; set; }
    public bool IgnoreReactionPreviewCalculation { get; set; }
    public bool IgnoreWebSocketNotification { get; set; }
}