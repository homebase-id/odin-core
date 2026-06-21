using System;

namespace Odin.Services.AppNotifications.ClientNotifications;

/// <summary>
/// An <see cref="IClientNotification"/> that should be delivered only to connected sockets whose
/// authenticated app matches <see cref="TargetAppId"/>. Sockets of other apps (and unauthenticated
/// sockets) never receive it. Notifications that do not implement this interface are broadcast to
/// all sockets as before.
/// </summary>
public interface IAppTargetedClientNotification : IClientNotification
{
    Guid TargetAppId { get; }
}
