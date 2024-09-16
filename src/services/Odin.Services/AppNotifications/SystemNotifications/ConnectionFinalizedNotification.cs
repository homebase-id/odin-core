using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

/// <summary>
/// System notification used to handle operations after a connection between two identities is established
/// </summary>
public class ConnectionFinalizedNotification : MediatorNotificationBase
{
    /// <summary>
    /// The identity with which the connection was established
    /// </summary>
    public OdinId OdinId { get; init; }
    
    public DatabaseConnection DatabaseConnection { get; init; }
}