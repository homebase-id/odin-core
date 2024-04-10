using System;
using Odin.Services.AppNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class DriveFileAddedNotification(OdinContext context) : MediatorNotificationBase(context), IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileAdded;

    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
}