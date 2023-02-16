using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Core.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
   
    public InternalDriveFileId File { get; set; }
    
    public ServerFileHeader ServerFileHeader { get; set; }
    
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}