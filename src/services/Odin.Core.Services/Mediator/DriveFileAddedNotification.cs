using System;
using MediatR;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Mediator;

public class DriveFileAddedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileAdded;

    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
    
    // public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}

public class ReactionPreviewUpdatedNotification : EventArgs, INotification, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.StatisticsChanged;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;

    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}
