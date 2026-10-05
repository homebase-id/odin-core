using Odin.Core.Time;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

/// <summary>
/// What the outbox processor should do with an item after a worker has had a go at it.
/// </summary>
/// <param name="ShouldMarkComplete">Remove the item from the outbox: sent, or given up on.</param>
/// <param name="NextRun">When to try again, when the item stays in the outbox.</param>
/// <param name="SpendsAttempt">
/// False when the recipient asked us to come back later (see <see cref="OutboxRetryLater"/>): the item
/// is rescheduled without counting against <c>Host:OutboxOperationMaxAttempts</c>, so a paused or
/// out-of-quota recipient does not exhaust the attempt budget in a few hours.
/// </param>
public readonly record struct OutboxProcessingResult(bool ShouldMarkComplete, UnixTimeUtc NextRun, bool SpendsAttempt)
{
    public static OutboxProcessingResult Complete() => new(true, UnixTimeUtc.ZeroTime, true);

    public static OutboxProcessingResult Retry(UnixTimeUtc nextRun) => new(false, nextRun, true);

    public static OutboxProcessingResult RetryLater(UnixTimeUtc nextRun) => new(false, nextRun, false);
}
