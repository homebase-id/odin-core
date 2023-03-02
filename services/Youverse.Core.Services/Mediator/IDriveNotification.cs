using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Mediator;

public interface IDriveNotification : INotification
{
    ClientNotificationType NotificationType { get; }
    
    public DriveNotificationType DriveNotificationType { get; }
        
    public InternalDriveFileId File { get; set; }
        
    public ServerFileHeader ServerFileHeader { get; set; }
    
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

}

public enum DriveNotificationType
{
    FileAdded,
    FileModified,
    FileDeleted
}