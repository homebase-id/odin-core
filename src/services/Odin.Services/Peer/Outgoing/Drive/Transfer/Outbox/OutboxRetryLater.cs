using System;
using Odin.Core.Time;

#nullable enable

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

/// <summary>
/// Scheduling rules for a recipient that answered "retry later" (503 or 507 with a Retry-After
/// header): the identity is paused or out of quota, so the item waits rather than burning attempts.
/// </summary>
public static class OutboxRetryLater
{
    /// <summary>
    /// Floor on the delay we honour. Keeps a low (or malicious) Retry-After from turning into a tight
    /// resend loop; a paused identity asks for 10 minutes, which is also how fast a resumed identity
    /// gets its backlog.
    /// </summary>
    public static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Ceiling on the delay we honour, so a recipient cannot park an item for days at a time.
    /// </summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(6);

    public static UnixTimeUtc NextRun(TimeSpan retryAfter, UnixTimeUtc now)
    {
        var delay = retryAfter < MinDelay ? MinDelay : retryAfter > MaxDelay ? MaxDelay : retryAfter;
        return now.AddSeconds((int)delay.TotalSeconds);
    }

    /// <summary>
    /// True when the item has been waiting longer than <paramref name="maxAge"/> and should be given up on.
    /// </summary>
    public static bool IsExpired(UnixTimeUtc addedTimestamp, UnixTimeUtc now, TimeSpan maxAge)
    {
        return now.milliseconds - addedTimestamp.milliseconds >= (long)maxAge.TotalMilliseconds;
    }
}
