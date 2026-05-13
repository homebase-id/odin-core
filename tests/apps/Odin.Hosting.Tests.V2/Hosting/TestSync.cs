using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Tenant-scoped facade over <see cref="PeerOutboxProcessorBackgroundService"/> and
/// <see cref="PeerOutbox"/>. Registered at root container level by <see cref="OdinHost"/>;
/// tenant scopes resolve via parent-scope fallback. Exposes outbox drain + status reads only —
/// inbox processing is caller-scoped (Owner / App) and lives on the per-caller <see cref="HttpInboxSync{TRefit}"/>
/// subclasses, which route through the production HTTP endpoint to get a real permissions context.
/// </summary>
internal sealed class TestSync(
    PeerOutboxProcessorBackgroundService outboxProcessor,
    PeerOutbox peerOutbox) : ITestSync
{
    public Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        => outboxProcessor.DrainAsync(cancellationToken);

    public Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default)
        => Task.FromException<InboxStatus>(new NotSupportedException(
            "Direct ProcessInboxAsync isn't supported on the host-level sync — it needs a real " +
            "caller context. Use OwnerSession.Sync / AppSession.Sync (which routes through the " +
            "production HTTP endpoint with the caller's permissions)."));

    public async Task<bool> IsOutboxEmptyAsync(TargetDrive drive)
    {
        var status = await peerOutbox.GetOutboxStatusAsync(drive.Alias);
        return status.TotalItems == 0 && status.CheckedOutCount == 0;
    }

    public async Task WaitForOutboxEmptyAsync(TargetDrive drive, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var delay = 5;
        while (!linkedCts.IsCancellationRequested)
        {
            if (await IsOutboxEmptyAsync(drive))
            {
                return;
            }
            try
            {
                await Task.Delay(delay, linkedCts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            if (delay < 100) delay *= 2;
        }

        if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"WaitForOutboxEmptyAsync timed out after {effectiveTimeout.TotalSeconds:F0}s on drive {drive.Alias}");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
