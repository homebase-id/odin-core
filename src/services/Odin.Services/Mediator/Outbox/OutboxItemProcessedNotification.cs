using System;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Mediator.Outbox;

public class OutboxFileItemDeliverySuccessNotification(OdinContext context) : MediatorNotificationBase(context)
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxFileItemDeliverySuccess;

    public OdinId Recipient { get; set; }
    public InternalDriveFileId File { get; set; }

    /// <summary>
    /// The version tag of the File that was processed
    /// </summary>
    public Guid VersionTag { get; set; }

    public FileSystemType FileSystemType { get; set; }
}