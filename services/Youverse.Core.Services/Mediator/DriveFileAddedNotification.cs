using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator;


public class TransitFileReceivedNotification : EventArgs, INotification
{
    public ExternalFileIdentifier TempFile { get; set; }
}

public class DriveFileAddedNotification : EventArgs, INotification, IDriveClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;
    
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
}