using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Util;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Sender side (hops 1→2): fans an opaque live data point out to each connected recipient's peer
/// perimeter, fire-and-forget. No outbox, no retry, no durable storage — if a recipient is
/// unreachable the point is simply dropped (the next tick supersedes it).
/// </summary>
public class LiveRelayService : PeerServiceBase
{
    // Bound a hung/unreachable recipient so it can't stall the fan-out for the whole tick.
    private static readonly TimeSpan PerRecipientTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<LiveRelayService> _logger;
    private readonly ILifetimeScope _lifetimeScope;

    public LiveRelayService(
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        ILogger<LiveRelayService> logger,
        ILifetimeScope lifetimeScope)
        : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        _logger = logger;
        _lifetimeScope = lifetimeScope;
    }

    public async Task RelayAsync(LiveRelayRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(request.ChannelKey, nameof(request.ChannelKey));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Blob, nameof(request.Blob));
        OdinValidationUtils.AssertValidRecipientList(request.Recipients, allowEmpty: false);

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

        var appId = odinContext.Caller.OdinClientContext?.AppId?.Value;
        if (!appId.HasValue)
        {
            throw new OdinClientException("Live relay requires an app context");
        }

        var envelope = new LiveRelayPeerEnvelope
        {
            ChannelKey = request.ChannelKey,
            Blob = request.Blob,
            AppId = appId.Value
        };

        var recipients = request.Recipients.ToOdinIdList().Distinct().ToList();

        // Fan out in parallel; each branch gets its own lifetime scope so concurrent DB access
        // (ICR resolution) doesn't trip the per-scope ScopedConnectionFactory parallelism guard.
        var tasks = recipients.Select(recipient => SendFireAndForgetAsync(recipient, envelope, odinContext));
        await Task.WhenAll(tasks);
    }

    private async Task SendFireAndForgetAsync(OdinId recipient, LiveRelayPeerEnvelope envelope, IOdinContext odinContext)
    {
        try
        {
            await using var childScope = _lifetimeScope.BeginLifetimeScope($"LiveRelay:{Guid.NewGuid()}");
            var svc = childScope.Resolve<LiveRelayService>();
            await svc.SendOneAsync(recipient, envelope, odinContext);
        }
        catch (Exception e)
        {
            // Fire-and-forget: an unreachable/erroring recipient is dropped, never surfaced.
            _logger.LogDebug(e, "Live relay to {recipient} dropped: {error}", recipient, e.Message);
        }
    }

    /// <summary>
    /// Sends one envelope to one recipient. Public so a child-scoped instance can be invoked from
    /// <see cref="SendFireAndForgetAsync"/>; not part of the external surface.
    /// </summary>
    public async Task SendOneAsync(OdinId recipient, LiveRelayPeerEnvelope envelope, IOdinContext odinContext)
    {
        var (_, client) = await CreateHttpClientAsync<ILiveRelayHttpClient>(recipient, odinContext);
        using var cts = new CancellationTokenSource(PerRecipientTimeout);
        await client.Relay(envelope, cts.Token);
    }
}
