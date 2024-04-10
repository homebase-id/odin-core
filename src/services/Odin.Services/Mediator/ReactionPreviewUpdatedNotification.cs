using System;
using MediatR;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class ReactionPreviewUpdatedNotification(OdinContext context) : MediatorNotificationBase(context), IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.StatisticsChanged;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileModified;

    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
    public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }
}