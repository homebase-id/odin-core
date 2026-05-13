#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Owner-scoped sync facade exposed via <see cref="OwnerSession.Sync"/>. Delegates outbox drain
/// and status reads to the host-level <see cref="ITestSync"/> (direct service calls — no caller
/// context needed). Inbox processing instead goes through the owner's HTTP endpoint so the
/// request runs under the owner's full permissions context: the synthetic system context that
/// <see cref="TestSync"/> builds lacks drive grants and trips
/// <c>PermissionContext.GetTargetDrive</c> on flows that look files up by GTID (peer delete /
/// read-receipt inbox items).
/// </summary>
public sealed class OwnerSync : ITestSync
{
    private readonly ITestSync _hostSync;
    private readonly OwnerSession _owner;

    internal OwnerSync(ITestSync hostSync, OwnerSession owner)
    {
        _hostSync = hostSync;
        _owner = owner;
    }

    public Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        => _hostSync.DrainOutboxAsync(cancellationToken);

    public Task<bool> IsOutboxEmptyAsync(TargetDrive drive)
        => _hostSync.IsOutboxEmptyAsync(drive);

    public Task WaitForOutboxEmptyAsync(TargetDrive drive, CancellationToken cancellationToken = default)
        => _hostSync.WaitForOutboxEmptyAsync(drive, cancellationToken);

    public async Task<InboxStatus> ProcessInboxAsync(
        TargetDrive drive,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var (client, sharedSecret) = _owner.NewAdminHttpClient();
        try
        {
            var svc = RefitCreator.RestServiceFor<IOwnerInboxProcessorRefit>(client, sharedSecret);
            var resp = await svc.ProcessInbox(new ProcessInboxRequest { TargetDrive = drive, BatchSize = batchSize });
            if (!resp.IsSuccessStatusCode)
            {
                throw new System.InvalidOperationException(
                    $"ProcessInbox HTTP failed: {resp.StatusCode}");
            }
            return resp.Content!;
        }
        finally
        {
            client.Dispose();
        }
    }
}

internal interface IOwnerInboxProcessorRefit
{
    [Post("/transit/inbox/processor/process")]
    Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);
}
