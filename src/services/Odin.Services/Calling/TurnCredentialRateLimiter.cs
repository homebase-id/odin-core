using System;
using System.Collections.Generic;

namespace Odin.Services.Calling;

/// <summary>
/// How many TURN logins each app of one identity may get per minute, on this node. One instance per
/// tenant, one budget per app, so one app looping cannot use up the chat app's. A device asks once per
/// call and once per ICE restart, so the limit only stops something that keeps asking.
/// </summary>
/// <remarks>
/// Not System.Threading.RateLimiting: Odin.Services does not reference it. The caller passes a
/// monotonic clock (<see cref="Environment.TickCount64"/>), so a wall-clock step cannot freeze the window.
/// </remarks>
public sealed class TurnCredentialRateLimiter
{
    public const int PermitsPerMinute = 20;
    private const long WindowMs = 60_000;

    private readonly Dictionary<Guid, (long WindowStartMs, int Count)> _byApp = new();

    /// <summary>True when <paramref name="appId"/> may get another login at <paramref name="nowMs"/>.</summary>
    public bool TryAcquire(Guid appId, long nowMs)
    {
        lock (_byApp)
        {
            if (!_byApp.TryGetValue(appId, out var window) || nowMs - window.WindowStartMs >= WindowMs)
            {
                window = (nowMs, 0);
            }

            if (window.Count >= PermitsPerMinute)
            {
                return false;
            }

            _byApp[appId] = (window.WindowStartMs, window.Count + 1);
            return true;
        }
    }
}
