using System;
using Odin.Core.Time;

namespace Odin.Services.Calling;

/// <summary>
/// How many TURN logins one identity may get per minute, on this node. One instance per tenant.
/// A device asks once per call and once per ICE restart, so the limit only stops a script that
/// keeps asking with an app token.
/// </summary>
/// <remarks>
/// Not System.Threading.RateLimiting: Odin.Services does not reference it, and its limiters read the
/// wall clock, where this one takes <c>now</c> so the window and Retry-After can be tested exactly.
/// </remarks>
public sealed class TurnCredentialRateLimiter
{
    public const int PermitsPerMinute = 20;
    private const long WindowMs = 60_000;

    private readonly object _lock = new();
    private long _windowStartMs;
    private int _count;

    /// <summary>True when a login may be issued; otherwise how long until the next one may.</summary>
    public bool TryAcquire(UnixTimeUtc now, out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            if (now.milliseconds - _windowStartMs >= WindowMs)
            {
                _windowStartMs = now.milliseconds;
                _count = 0;
            }

            if (_count < PermitsPerMinute)
            {
                _count++;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            retryAfter = TimeSpan.FromMilliseconds(_windowStartMs + WindowMs - now.milliseconds);
            return false;
        }
    }
}
