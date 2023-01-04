using System;
using MediatR;
using Youverse.Core.Serialization;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
    public InternalDriveFileId File { get; set; }

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