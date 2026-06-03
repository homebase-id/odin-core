#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Caller-scoped sync facade for the V2 test framework. Outbox drain / status methods delegate to
/// the host-level <see cref="ITestSync"/> (direct service calls — no caller context needed).
/// <see cref="ProcessInboxAsync"/> instead goes through the caller's V1 inbox-processor HTTP
/// endpoint so the request runs under the caller's real permissions context, which matters for
/// flows that look files up by GTID (peer delete / read-receipt inbox items). The synthetic system
/// context that <see cref="TestSync"/> builds for direct calls lacks drive grants and trips
/// <c>PermissionContext.GetTargetDrive</c> on those paths.
/// </summary>
/// <remarks>
/// Concrete impls (<see cref="OwnerSync"/>, <see cref="AppSync"/>) supply the per-caller Refit
/// interface bound to their endpoint path. A fresh <see cref="HttpClient"/> is built per call and
/// disposed when the call returns. The cost is one delegating-handler chain allocation; the inner
/// TestServer handler is cached by <see cref="InProcessApiClientFactory"/> so we don't re-create
/// the whole pipeline.
/// </remarks>
public abstract class HttpInboxSync<TRefit> : ITestSync where TRefit : class
{
    private readonly ITestSync _hostSync;
    private readonly IV2Caller _caller;

    internal HttpInboxSync(ITestSync hostSync, IV2Caller caller)
    {
        _hostSync = hostSync;
        _caller = caller;
    }

    public Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        => _hostSync.DrainOutboxAsync(cancellationToken);

    public Task<bool> IsOutboxEmptyAsync(TargetDrive drive)
        => _hostSync.IsOutboxEmptyAsync(drive);

    public async Task<InboxStatus> ProcessInboxAsync(
        TargetDrive drive,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        using var http = _caller.Factory.CreateHttpClient(_caller.Identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<TRefit>(http, sharedSecret);
        var resp = await CallProcessInboxAsync(svc, new ProcessInboxRequest { TargetDrive = drive, BatchSize = batchSize });
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ProcessInbox HTTP failed: {resp.StatusCode}");
        }
        return resp.Content!;
    }

    /// <summary>Invoke the per-caller Refit interface's <c>ProcessInbox</c> method.</summary>
    protected abstract Task<ApiResponse<InboxStatus>> CallProcessInboxAsync(TRefit svc, ProcessInboxRequest request);
}
