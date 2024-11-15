using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

/// <summary>
/// System notification used to handle clean up when a connection is blocked
/// </summary>
public class ConnectionBlockedNotification : MediatorNotificationBase
{
    /// <summary>
    /// The identity that was blocked
    /// </summary>
    public OdinId OdinId { get; init; }
    
    public IdentityDatabase db { get; init; }
}