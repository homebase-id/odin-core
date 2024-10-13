using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

/// <summary>
/// System notification used to handle operations after a connection between two identities is established
/// </summary>
public class ConnectionFinalizedNotification : MediatorNotificationBase, IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.ConnectionFinalized;

    public Guid NotificationTypeId { get; } = Guid.Parse("abf864e7-f99d-464c-b7a4-58bd42530294");

    /// <summary>
    /// The identity with which the connection was established
    /// </summary>
    public OdinId OdinId { get; init; }

    public IdentityDatabase db { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            Identity = this.OdinId.DomainName
        });
    }
}