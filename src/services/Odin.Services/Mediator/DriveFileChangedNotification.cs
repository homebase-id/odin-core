using System;
using MediatR;
using Odin.Services.AppNotifications;
using Odin.Services.Apps;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification, IDriveNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;
        
        public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;
        
        public InternalDriveFileId File { get; set; }
        public ServerFileHeader ServerFileHeader { get; set; }
        
        public ExternalFileIdentifier ExternalFile { get; set; }
        
    }
}