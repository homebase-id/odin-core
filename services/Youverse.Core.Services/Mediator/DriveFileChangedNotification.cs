using System;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification, IDriveClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;

        public InternalDriveFileId File { get; set; }
        public ServerFileHeader ServerFileHeader { get; set; }

        public ExternalFileIdentifier ExternalFile { get; set; }
        
    }
}