using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
    
    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileDeleted;

    public bool IsHardDelete { get; set; }
    
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }

    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}