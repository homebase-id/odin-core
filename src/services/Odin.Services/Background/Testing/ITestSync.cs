using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Background.Testing;

/// <summary>
/// Test-only synchronous drain hooks for the peer outbox / inbox. Registered conditionally when
/// <c>OdinConfiguration.Testing.EnableSyncHooks</c> is true (set by the V2 in-process test
/// framework; never by production). Lets tests bypass the background timer loop and call the
/// processors directly instead of polling drive status over HTTP.
/// </summary>
/// <remarks>
/// MediatR notifications are intentionally not represented here: Odin uses the default
/// <c>ForeachAwait</c> publisher, which runs all handlers synchronously inside the request that
/// fires the notification. There is nothing to "flush" between a write and the next read.
/// </remarks>
public interface ITestSync
{
    /// <summary>
    /// Drains every pending outbox item for this tenant. Returns when the outbox is empty.
    /// </summary>
    /// <remarks>
    /// Without in-process peer routing, actual peer sends still go to a real network address that
    /// doesn't resolve in tests — items will go through the failure-and-reschedule path. Useful
    /// today only as scaffolding; becomes load-bearing once peer routing lands.
    /// </remarks>
    Task DrainOutboxAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending inbox items for <paramref name="drive"/>
    /// using a synthetic system caller context. Equivalent to the V1
    /// <c>POST /api/owner/v1/peer/inbox/processor/process</c> endpoint without the HTTP hop.
    /// </summary>
    Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>True when both <c>TotalItems</c> and <c>CheckedOutCount</c> are 0 on the drive's outbox.</summary>
    Task<bool> IsOutboxEmptyAsync(TargetDrive drive);

    /// <summary>
    /// Polls <see cref="IsOutboxEmptyAsync"/> with a short back-off until the outbox empties or
    /// <paramref name="cancellationToken"/> fires. No HTTP — reads the local outbox table directly.
    /// </summary>
    Task WaitForOutboxEmptyAsync(TargetDrive drive, CancellationToken cancellationToken = default);
}
