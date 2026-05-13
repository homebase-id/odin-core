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
/// interface bound to their endpoint path. The Refit proxy + HttpClient are cached on first use.
/// </remarks>
public abstract class HttpInboxSync<TRefit> : ITestSync where TRefit : class
{
    private readonly ITestSync _hostSync;
    private readonly IV2Caller _caller;

    private readonly object _lock = new();
    private TRefit? _refitClient;
    private HttpClient? _http;

    internal HttpInboxSync(ITestSync hostSync, IV2Caller caller)
    {
        _hostSync = hostSync;
        _caller = caller;
    }

    public Task DrainOutboxAsync(CancellationToken cancellationToken = default)
        => _hostSync.DrainOutboxAsync(cancellationToken);

    public Task<bool> IsOutboxEmptyAsync(TargetDrive drive)
        => _hostSync.IsOutboxEmptyAsync(drive);

    public Task WaitForOutboxEmptyAsync(TargetDrive drive, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _hostSync.WaitForOutboxEmptyAsync(drive, timeout, cancellationToken);

    public async Task<InboxStatus> ProcessInboxAsync(
        TargetDrive drive,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var svc = GetOrBuildClient();
        var resp = await CallProcessInboxAsync(svc, new ProcessInboxRequest { TargetDrive = drive, BatchSize = batchSize });
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ProcessInbox HTTP failed: {resp.StatusCode}");
        }
        return resp.Content!;
    }

    /// <summary>Invoke the per-caller Refit interface's <c>ProcessInbox</c> method.</summary>
    protected abstract Task<ApiResponse<InboxStatus>> CallProcessInboxAsync(TRefit svc, ProcessInboxRequest request);

    private TRefit GetOrBuildClient()
    {
        if (_refitClient != null) return _refitClient;
        lock (_lock)
        {
            if (_refitClient != null) return _refitClient;
            _http = _caller.Factory.CreateHttpClient(_caller.Identity, out var sharedSecret);
            _refitClient = RefitCreator.RestServiceFor<TRefit>(_http, sharedSecret);
            return _refitClient;
        }
    }
}
