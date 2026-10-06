using System;
using System.Net;
using Odin.Core.Time;
using Refit;

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

    /// <summary>
    /// The Retry-After the recipient asked for, when it answered "come back later" (503 while paused,
    /// 507 while out of quota). Null for every other response, including a 503 without the header.
    /// </summary>
    public static TimeSpan? RetryAfterFrom<T>(ApiResponse<T> response)
    {
        if (response.StatusCode is not (HttpStatusCode.ServiceUnavailable or HttpStatusCode.InsufficientStorage))
        {
            return null;
        }

        var retryAfter = response.Headers?.RetryAfter;
        if (retryAfter?.Delta != null)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter?.Date != null)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    public static UnixTimeUtc NextRun(TimeSpan retryAfter, UnixTimeUtc now)
    {
        var delay = retryAfter < MinDelay ? MinDelay : retryAfter > MaxDelay ? MaxDelay : retryAfter;
        return now.AddMilliseconds((long)delay.TotalMilliseconds);
    }

    /// <summary>
    /// True when the item has been waiting longer than <paramref name="maxAge"/> and should be given up on.
    /// </summary>
    public static bool IsExpired(UnixTimeUtc addedTimestamp, UnixTimeUtc now, TimeSpan maxAge)
    {
        return now - addedTimestamp >= maxAge;
    }
}
