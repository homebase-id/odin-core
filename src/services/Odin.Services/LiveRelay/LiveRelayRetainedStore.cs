using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Time;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Holds the last live-relay data point per (channel, sender), per app, for a tenant. Ephemeral and
/// last-value-wins — there is no durable storage. Backed by the transparent layer-2 cache
/// (<see cref="ITenantLevel2Cache{T}"/>: in-memory L1 + Redis L2, memory-only when Redis is off),
/// so any server instance can read the set to flush current state to a (re)connecting client.
///
/// Registered as a per-tenant singleton so the per-app <see cref="SemaphoreSlim"/> serializes the
/// read-modify-write of a snapshot value within an instance. Across instances the FusionCache Redis
/// backplane keeps L1 coherent; the only residual is a rare concurrent-write window, which is
/// self-healing for last-value-wins data.
/// </summary>
public sealed class LiveRelayRetainedStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ITenantLevel2Cache<LiveRelayRetainedStore> _cache;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public LiveRelayRetainedStore(ITenantLevel2Cache<LiveRelayRetainedStore> cache)
    {
        _cache = cache;
    }

    public async Task PutAsync(
        Guid appId,
        Guid channelKey,
        OdinId sender,
        string blob,
        UnixTimeUtc receivedAt,
        CancellationToken cancellationToken = default)
    {
        var sem = _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        try
        {
            var key = CacheKey(appId);
            var snapshot = await _cache.GetOrDefaultAsync<LiveRelayAppSnapshot>(key, cancellationToken: cancellationToken)
                           ?? new LiveRelayAppSnapshot();

            PruneExpired(snapshot);

            snapshot.Entries[SlotKey(channelKey, sender)] = new LiveRelayRetainedEntry
            {
                Blob = blob,
                AppId = appId,
                ChannelKey = channelKey,
                SenderDomain = sender.DomainName,
                ReceivedAtMs = receivedAt.milliseconds
            };

            await _cache.SetAsync(key, snapshot, Ttl, cancellationToken: cancellationToken);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Returns every non-expired sender's last data point for the given app in this tenant.
    /// </summary>
    public async Task<List<LiveRelayRetainedEntry>> GetAllForAppAsync(
        Guid appId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetOrDefaultAsync<LiveRelayAppSnapshot>(
            CacheKey(appId), cancellationToken: cancellationToken);

        if (snapshot == null)
        {
            return new List<LiveRelayRetainedEntry>();
        }

        var cutoff = UnixTimeUtc.Now().milliseconds - (long)Ttl.TotalMilliseconds;
        return snapshot.Entries.Values.Where(e => e.ReceivedAtMs >= cutoff).ToList();
    }

    private static void PruneExpired(LiveRelayAppSnapshot snapshot)
    {
        var cutoff = UnixTimeUtc.Now().milliseconds - (long)Ttl.TotalMilliseconds;
        var stale = snapshot.Entries.Where(kvp => kvp.Value.ReceivedAtMs < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var k in stale)
        {
            snapshot.Entries.Remove(k);
        }
    }

    private static string CacheKey(Guid appId) => $"live-relay:{appId}";

    private static string SlotKey(Guid channelKey, OdinId sender) => $"{channelKey}:{sender.DomainName}";
}
