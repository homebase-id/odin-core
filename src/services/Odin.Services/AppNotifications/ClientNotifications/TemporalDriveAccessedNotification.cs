using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    /// <summary>
    /// Raised on the owner's server when a connected identity reads a drive through the temporal
    /// (time-boxed) read API. Surfaces to the owner as a push/app notification and is the durable
    /// access record.
    /// </summary>
    public class TemporalDriveAccessedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.TemporalDriveAccessed;

        public Guid NotificationTypeId { get; } = Guid.Parse("b6f6a8f2-3a1e-4c2a-9d3e-2f0a7c9e1b44");

        /// <summary>
        /// The connected identity that accessed the drive.
        /// </summary>
        public OdinId Accessor { get; init; }

        /// <summary>
        /// The drive that was accessed.
        /// </summary>
        public TargetDrive Drive { get; init; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                Accessor = this.Accessor.DomainName,
                Drive = this.Drive
            });
        }
    }
}
