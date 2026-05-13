using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Thin facade over <see cref="PeerOutboxProcessorBackgroundService"/>, <see cref="PeerInboxProcessor"/>,
/// and <see cref="PeerOutbox"/>. Registered at root container level by <see cref="OdinHost"/>;
/// tenant scopes resolve via parent-scope fallback. Constructs a system caller context internally
/// so tests don't have to plumb an <c>IOdinContext</c> through.
/// </summary>
internal sealed class TestSync(
    PeerOutboxProcessorBackgroundService outboxProcessor,
    PeerInboxProcessor inboxProcessor,
    PeerOutbox peerOutbox,
    TenantContext tenantContext) : ITestSync
{
    public Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        => outboxProcessor.DrainAsync(cancellationToken);

    public Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default)
        => inboxProcessor.ProcessInboxAsync(drive, BuildSystemContext(), batchSize);

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

    /// <summary>
    /// Mirrors the synthetic system context built by
    /// <c>PeerOutboxProcessorBackgroundService.ProcessItemThread</c>: tenant-owned, system security
    /// level, master-key-less. Enough for the inbox processor's storage path.
    /// </summary>
    private IOdinContext BuildSystemContext()
    {
        var ctx = new OdinContext
        {
            Tenant = tenantContext.HostOdinId,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.System,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };
        ctx.SetPermissionContext(new PermissionContext(null, null, true));
        return ctx;
    }
}
