using System;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator.Outbox;

public class OutboxItemDeliverySuccessNotification : EventArgs, INotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxItemDeliverySuccess;

    public OdinId Recipient { get; set; }
    public InternalDriveFileId File { get; set; }

    /// <summary>
    /// The version tag of the File that was processed
    /// </summary>
    public Guid VersionTag { get; set; }

    public FileSystemType FileSystemType { get; set; }
    
}

public class OutboxItemDeliveryFailedNotification : EventArgs, INotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.OutboxItemDeliveryFailed;

    public OdinId Recipient { get; set; }
    public InternalDriveFileId File { get; set; }
    
    public FileSystemType FileSystemType { get; set; }
    
    public LatestProblemStatus ProblemStatus { get; set; }
}