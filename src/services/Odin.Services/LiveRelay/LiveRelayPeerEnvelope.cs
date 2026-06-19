using System;

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
}
