using System;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification, IDriveNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;

        public InternalDriveFileId File { get; set; }
        public ServerFileHeader ServerFileHeader { get; set; }
        
        //TODO: revisit having put this field on here.  it might be not perform well
        public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

        public ExternalFileIdentifier ExternalFile { get; set; }
        
    }
}