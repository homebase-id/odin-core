using System;

namespace Odin.Services.AppNotifications.ClientNotifications;

/// <summary>
/// An <see cref="IClientNotification"/> that should be delivered only to connected sockets whose
/// authenticated app matches <see cref="TargetAppId"/>, or, for <see cref="OwnersOwnClient"/>, only to the
/// owner's sockets of no app. Sockets of other apps (and unauthenticated sockets) never receive it.
/// Notifications that do not implement this interface are broadcast to all sockets as before.
/// </summary>
public interface IAppTargetedClientNotification : IClientNotification
{
    /// <summary>
    /// The target for the owner's own client, the sockets of no app: an id no app has, so a node that doesn't know it
    /// delivers to none rather than to all
    /// </summary>
    static readonly Guid OwnersOwnClient = Guid.Parse("232eb023-8449-4a4a-9e25-857892eb4046");

    Guid TargetAppId { get; }
}
