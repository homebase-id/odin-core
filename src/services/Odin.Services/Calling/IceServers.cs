using System.Collections.Generic;
using System.Text.Json.Serialization;
using Odin.Core.Time;

namespace Odin.Services.Calling;

public class IceServersRequest
{
    /// <summary>The identity being called, or calling. Must be connected.</summary>
    public string Peer { get; init; }
}

/// <summary>One entry of WebRTC's <c>RTCConfiguration.iceServers</c>, passed through as given.</summary>
public class IceServer
{
    public IReadOnlyList<string> Urls { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Username { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Credential { get; init; }
}

public class IceServersResponse
{
    public IReadOnlyList<IceServer> IceServers { get; init; }

    /// <summary>
    /// When the TURN login stops opening new relays; null without one (no TURN on this host, or this
    /// app asked for more than <see cref="TurnCredentialRateLimiter.PermitsPerMinute"/> in a minute). A call already relayed
    /// keeps going; ask again before an ICE restart.
    /// </summary>
    public UnixTimeUtc? ExpiresAt { get; init; }
}
