using MediatR;
using Odin.Services.AppNotifications;
using Odin.Services.Apps;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public interface IDriveNotification : INotification
{
    ClientNotificationType NotificationType { get; }

    public DriveNotificationType DriveNotificationType { get; }
    public InternalDriveFileId File { get; init; }

    public ServerFileHeader ServerFileHeader { get; init; }

    public OdinContext OdinContext { get; init; }
    // public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}

public enum DriveNotificationType
{
    FileAdded,
    FileModified,
    FileDeleted
}