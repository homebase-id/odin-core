using Odin.Core.Identity;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

public class ConnectionDeletedNotification : MediatorNotificationBase
{
    /// <summary>
    /// The identity that was deleted
    /// </summary>
    public OdinId OdinId { get; init; }
}