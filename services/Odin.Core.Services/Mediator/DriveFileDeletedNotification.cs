using System;
using MediatR;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
    
    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileDeleted;

    public bool IsHardDelete { get; set; }
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }

    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}