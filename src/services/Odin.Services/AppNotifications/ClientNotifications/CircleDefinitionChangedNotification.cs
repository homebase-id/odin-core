using System;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public enum CircleDefinitionChangeType
    {
        Created = 1,
        Updated = 2,
        Deleted = 3,
        Enabled = 4,
        Disabled = 5
    }

    /// <summary>
    /// Pushed to the owner's own sessions when a circle definition is created, renamed/re-permissioned (updated),
    /// deleted, enabled, or disabled.  Lets other devices invalidate their cached circle definitions and re-fetch.
    /// </summary>
    public class CircleDefinitionChangedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.CircleDefinitionChanged;

        public Guid NotificationTypeId { get; } = Guid.Parse("b7c3e6d2-1a48-4f9b-bc25-7e3a9d0f5c18");

        /// <summary>
        /// The circle definition that changed
        /// </summary>
        public Guid CircleId { get; init; }

        /// <summary>
        /// What changed about the circle definition
        /// </summary>
        public CircleDefinitionChangeType Change { get; init; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                CircleId = this.CircleId,
                Change = this.Change.ToString()
            });
        }
    }
}
