using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public enum ConnectionChangeType
    {
        Disconnected = 1,
        Blocked = 2,
        Unblocked = 3,
        CircleGranted = 4,
        CircleRevoked = 5
    }

    /// <summary>
    /// Pushed to the owner's own sessions when an existing connection's state changes (disconnect/block/unblock)
    /// or a circle is granted/revoked to that connection.  Lets other devices invalidate their cached connection
    /// state and re-fetch the affected identity.
    /// </summary>
    public class ConnectionChangedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionChanged;

        public Guid NotificationTypeId { get; } = Guid.Parse("4f8d2a1c-9b3e-4c7a-8f21-2d6b5e9c1a40");

        /// <summary>
        /// The connection that changed
        /// </summary>
        public OdinId OdinId { get; init; }

        /// <summary>
        /// What changed about the connection
        /// </summary>
        public ConnectionChangeType Change { get; init; }

        /// <summary>
        /// The affected circle when <see cref="Change"/> is <see cref="ConnectionChangeType.CircleGranted"/> or
        /// <see cref="ConnectionChangeType.CircleRevoked"/>; otherwise null.
        /// </summary>
        public Guid? CircleId { get; init; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                Identity = this.OdinId.DomainName,
                Change = this.Change.ToString(),
                CircleId = this.CircleId
            });
        }
    }
}
