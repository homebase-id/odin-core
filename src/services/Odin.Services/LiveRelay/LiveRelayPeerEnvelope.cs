using System;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Server-to-server wire payload (hop 2). The sender identity is NOT carried here — the recipient
/// learns it for certain from the mutual-TLS peer cert.
/// </summary>
public class LiveRelayPeerEnvelope
{
    public Guid ChannelKey { get; init; }

    /// <summary>Opaque, app-encrypted bytes (base64). Never interpreted by the server.</summary>
    public string Blob { get; init; }

    /// <summary>The app the data is scoped to (inferred from the sender's app token on hop 1).</summary>
    public Guid AppId { get; init; }

    /// <summary>
    /// Optional push to enqueue on the recipient. Both ends pass it through
    /// <see cref="LiveRelayPush.Sanitize"/>, so only the allowlisted fields exist on the wire and
    /// the recipient's app id is always <see cref="AppId"/>, never the sender's claim.
    /// </summary>
    public AppNotificationOptions Push { get; init; }
}
