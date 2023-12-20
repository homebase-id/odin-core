using System;
using MediatR;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Mediator
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