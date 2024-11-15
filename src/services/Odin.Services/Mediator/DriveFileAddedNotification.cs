using System;
using MediatR;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public class DriveFileAddedNotification : MediatorNotificationBase, IDriveNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;

    public DriveNotificationType DriveNotificationType { get; } = DriveNotificationType.FileAdded;

    public InternalDriveFileId File { get; init; }

    public ServerFileHeader ServerFileHeader { get; init; }

    // public SharedSecretEncryptedFileHeader SharedSecretEncryptedFileHeader { get; set; }

    public IdentityDatabase db { get; init; }
    public bool IgnoreFeedDistribution { get; set; }
    public bool IgnoreReactionPreviewCalculation { get; set; }
}