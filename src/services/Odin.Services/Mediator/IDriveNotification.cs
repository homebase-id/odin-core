using MediatR;
using Odin.Core.Storage.SQLite;
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

    public IOdinContext OdinContext { get; init; }
    // public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

    /// <summary>
    /// Feed hack so I can ensure certain update events do not get distributed 
    /// </summary>
    public bool IgnoreFeedDistribution { get; set; }
    
    public bool IgnoreReactionPreviewCalculation { get; set; }

}

public enum DriveNotificationType
{
    FileAdded,
    FileModified,
    FileDeleted
}