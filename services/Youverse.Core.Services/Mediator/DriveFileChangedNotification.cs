using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveDefinitionAddedNotification : EventArgs, INotification
    {
        public bool IsNewDrive { get; set; }
        public StorageDrive Drive { get; set; }
    }

    public class DriveFileChangedNotification : EventArgs, INotification, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;
        public InternalDriveFileId File { get; set; }

        public ServerFileHeader FileHeader { get; set; }
    }

    public class DriveFileDeletedNotification : EventArgs, INotification, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
        public InternalDriveFileId File { get; set; }
    }
}