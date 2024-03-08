using System;
using MediatR;
using Odin.Services.AppNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
    
    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileDeleted;

    public bool IsHardDelete { get; set; }
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }

    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
    public ServerFileHeader PreviousServerFileHeader { get; set; }
    
    
}