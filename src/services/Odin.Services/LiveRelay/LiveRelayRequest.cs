using System;
using System.Collections.Generic;

namespace Odin.Services.LiveRelay;

/// <summary>
/// App-initiated request (hop 1): "share this opaque live data point with these connected
/// identities on this channel". The app does not declare its own AppId — it is inferred from the
/// authenticated caller.
/// </summary>
public class LiveRelayRequest
{
    /// <summary>Per-share-session key the participants agreed on; only routing, never interpreted.</summary>
    public Guid ChannelKey { get; init; }

    /// <summary>The connected identities to share with.</summary>
    public List<string> Recipients { get; init; } = new();

    /// <summary>Opaque, app-encrypted bytes (base64). Never interpreted by the server.</summary>
    public string Blob { get; init; }
}
