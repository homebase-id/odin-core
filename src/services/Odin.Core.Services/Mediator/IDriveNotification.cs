using MediatR;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Mediator;

public interface IDriveNotification : INotification
{
    ClientNotificationType NotificationType { get; }
    
    public DriveNotificationType DriveNotificationType { get; }
    public InternalDriveFileId File { get; set; }
        
    public ServerFileHeader ServerFileHeader { get; set; }
    
    // public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

}

public enum DriveNotificationType
{
    FileAdded,
    FileModified,
    FileDeleted
}