using MediatR;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Mediator;

public interface IDriveNotification : INotification
{
    ClientNotificationType NotificationType { get; }

    public DriveNotificationType DriveNotificationType { get; }
    public InternalDriveFileId File { get; init; }

    public ServerFileHeader ServerFileHeader { get; init; }

    public IOdinContext OdinContext { get; init; }

    /// <summary>
    /// Feed hack so I can ensure certain update events do not get distributed 
    /// </summary>
    public bool IgnoreFeedDistribution { get; set; }

    public bool IgnoreReactionPreviewCalculation { get; set; }

    /// <summary>
    /// When true, this drive event is NOT broadcast to WebSocket clients. Other handlers
    /// (cache invalidation, feed distribution, etc.) still run. Used to suppress the redundant
    /// local-reactions fileModified so a reaction emits a single client notification.
    /// </summary>
    public bool IgnoreWebSocketNotification { get; set; }

}

public enum DriveNotificationType
{
    FileAdded,
    FileModified,
    FileDeleted
}