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

namespace Odin.Services.Background.Testing;

/// <summary>
/// Thin facade over <see cref="PeerOutboxProcessorBackgroundService"/>, <see cref="PeerInboxProcessor"/>,
/// and <see cref="PeerOutbox"/>. Resolved from the tenant scope; constructs a system caller context
/// internally so tests don't have to plumb an <c>IOdinContext</c> through.
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

    public async Task WaitForOutboxEmptyAsync(TargetDrive drive, CancellationToken cancellationToken = default)
    {
        var delay = 5;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await IsOutboxEmptyAsync(drive))
            {
                return;
            }
            await Task.Delay(delay, cancellationToken);
            if (delay < 100) delay *= 2;
        }
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
