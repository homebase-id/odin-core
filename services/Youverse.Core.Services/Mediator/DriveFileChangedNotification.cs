using System;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileModified;
        public InternalDriveFileId File { get; set; }

        public ServerFileHeader FileHeader { get; set; }
        
        public string GetClientData()
        {
            return DotYouSystemSerializer.Serialize(new
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = this.File.FileId,
                    TargetDrive = null
                }
            });
        }
    }
}