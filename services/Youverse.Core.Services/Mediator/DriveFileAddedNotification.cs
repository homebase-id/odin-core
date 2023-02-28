using System;
using System.Collections.Generic;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Mediator;

public class DriveFileAddedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;
    
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}

public class StatisticsUpdatedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.StatisticsChanged;
    
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}
