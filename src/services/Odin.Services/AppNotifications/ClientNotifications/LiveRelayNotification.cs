using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications;

/// <summary>
/// A single opaque live-relay data point delivered to an app's connected sockets. The server does
/// not interpret <see cref="Blob"/> (it happens to be GPS, but could be anything). Delivered only
/// to sockets whose app matches <see cref="TargetAppId"/>.
/// </summary>
public class LiveRelayNotification : MediatorNotificationBase, IClientNotification, IAppTargetedClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.LiveRelay;

    public Guid NotificationTypeId { get; } = Guid.Parse("0d9a3f8e-2b6c-4d1a-9c5b-3e7f1a2b8c40");

    /// <summary>
    /// The identity that produced this data point (the sending server), known for certain from the
    /// mutual-TLS cert on the peer hop.
    /// </summary>
    public OdinId SenderOdinId { get; init; }

    /// <summary>
    /// The share-session key the sender used. The client routes by this to the correct open session.
    /// </summary>
    public Guid ChannelKey { get; init; }

    /// <summary>
    /// Opaque, app-encrypted bytes (base64). Never interpreted by the server.
    /// </summary>
    public string Blob { get; init; }

    /// <summary>
    /// When the recipient server received this data point (ms since unix epoch). Lets the client
    /// reason about freshness, especially for a point flushed on (re)connect.
    /// </summary>
    public UnixTimeUtc ReceivedAt { get; init; }

    /// <summary>
    /// The app this data is scoped to; only that app's sockets receive it.
    /// </summary>
    public Guid TargetAppId { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            SenderOdinId = this.SenderOdinId.DomainName,
            ChannelKey = this.ChannelKey,
            Blob = this.Blob,
            ReceivedAt = this.ReceivedAt.milliseconds
        });
    }
}
