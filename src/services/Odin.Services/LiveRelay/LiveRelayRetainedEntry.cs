using System;
using System.Collections.Generic;

namespace Odin.Services.LiveRelay;

/// <summary>
/// The last data point received from a single sender on a single channel. Serialized into the
/// layer-2 cache, so it must be a plain named type (no tuples/anonymous types).
/// </summary>
public sealed class LiveRelayRetainedEntry
{
    public string Blob { get; init; }
    public Guid AppId { get; init; }
    public Guid ChannelKey { get; init; }
    public string SenderDomain { get; init; }

    /// <summary>Server-received time, ms since unix epoch.</summary>
    public long ReceivedAtMs { get; init; }
}

/// <summary>
/// All retained last-data-points for one app in one tenant, keyed by "{channelKey}:{senderDomain}".
/// Stored as a single cache value so the whole set can be read in one shot for reconnect-flush.
/// </summary>
public sealed class LiveRelayAppSnapshot
{
    public Dictionary<string, LiveRelayRetainedEntry> Entries { get; init; } = new();
}
