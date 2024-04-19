using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator
{
    public class DriveFileChangedNotification : MediatorNotificationBase, IDriveNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;

        public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;

        public InternalDriveFileId File { get; init; }
        public ServerFileHeader ServerFileHeader { get; init; }

        public ExternalFileIdentifier ExternalFile { get; set; }
    }
}