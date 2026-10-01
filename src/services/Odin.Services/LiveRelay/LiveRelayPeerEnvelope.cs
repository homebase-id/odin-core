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
    /// Optional push to enqueue on the recipient. The recipient overwrites <c>AppId</c> with
    /// <see cref="AppId"/> and clears the fan-out fields; it trusts nothing else in here beyond
    /// what it validates.
    /// </summary>
    public AppNotificationOptions Push { get; init; }
}
