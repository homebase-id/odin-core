using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator.Outbox;

public class OutboxFileItemDeliveryFailedNotification(OdinContext context) : MediatorNotificationBase(context)
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxFileItemDeliveryFailed;

    public OdinId Recipient { get; set; }

    public InternalDriveFileId File { get; set; }

    public FileSystemType FileSystemType { get; set; }

    public LatestProblemStatus ProblemStatus { get; set; }
}