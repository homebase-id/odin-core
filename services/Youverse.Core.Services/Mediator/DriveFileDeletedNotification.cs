using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Mediator;

public class DriveFileDeletedNotification : EventArgs, INotification, IDriveClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileDeleted;
    public InternalDriveFileId File { get; set; }
    
}