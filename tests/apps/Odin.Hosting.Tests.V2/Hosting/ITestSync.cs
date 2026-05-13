using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Test-only synchronous drain hooks for the peer outbox / inbox. Registered by the V2 test
/// framework's <see cref="OdinHost"/> at root container level. Lets tests bypass the background
/// timer loop and call the processors instead of polling drive status. The impl reaches into
/// <c>PeerOutboxProcessorBackgroundService.DrainAsync</c> (internal in <c>Odin.Services</c>,
/// exposed via <c>InternalsVisibleTo</c>).
/// </summary>
/// <remarks>
/// MediatR notifications are intentionally not represented here: Odin uses the default
/// <c>ForeachAwait</c> publisher, which runs all handlers synchronously inside the request that
/// fires the notification. There is nothing to "flush" between a write and the next read.
/// </remarks>
public interface ITestSync
{
    /// <summary>
    /// Drains every pending outbox item for this tenant, waiting for each per-item worker to
    /// complete. Returns when the outbox is empty.
    /// </summary>
    /// <remarks>
    /// Peer-destined items are routed in-process via <c>TestPeerHttpClientFactory</c>, so this
    /// actually delivers; without that the items would go through the production failure-and-
    /// reschedule path.
    /// </remarks>
    Task DrainOutboxAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending inbox items for <paramref name="drive"/>.
    /// Owner sessions (via <c>OwnerSync</c>) route this through the
    /// <c>POST /api/owner/v1/transit/inbox/processor/process</c> endpoint so processing runs under
    /// the owner's permissions context — paths that look files up by GTID (peer delete, read
    /// receipt) need real drive grants. Direct (non-owner-routed) implementations build a
    /// synthetic system context which works only for the SaveFile path.
    /// </summary>
    Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>True when both <c>TotalItems</c> and <c>CheckedOutCount</c> are 0 on the drive's outbox.</summary>
    Task<bool> IsOutboxEmptyAsync(TargetDrive drive);

    /// <summary>
    /// Polls <see cref="IsOutboxEmptyAsync"/> with a short back-off until the outbox empties, the
    /// internal timeout expires (default 30s — override via <paramref name="timeout"/>), or
    /// <paramref name="cancellationToken"/> fires. Throws <see cref="System.TimeoutException"/> on
    /// timeout. No HTTP — reads the local outbox table directly.
    /// </summary>
    Task WaitForOutboxEmptyAsync(TargetDrive drive, System.TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
