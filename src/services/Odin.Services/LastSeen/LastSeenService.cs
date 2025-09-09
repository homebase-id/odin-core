using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Registry;

[assembly: InternalsVisibleTo("Odin.Services.Tests")]

namespace Odin.Services.LastSeen;

#nullable enable

public class LastSeenService(ISystemLevel2Cache<LastSeenService> cache, ILifetimeScope rootScope) : ILastSeenService
{
    private static readonly TimeSpan LastSeenEntryTtl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan UpdateThreshold = TimeSpan.FromSeconds(15);

    // SEB:NOTE we need this local lists to batch updates to the database
    // since the cache can't provide us with the list. Stupid cache.
    private readonly ConcurrentDictionary<Guid, UnixTimeUtc> _lastSeenByIdentityId = new();

    //

    private static string BuildCacheKey(Guid identityId) => "last-seen:" + identityId;

    //

    public Task LastSeenNowAsync(IdentityRegistration registration)
    {
        return PutLastSeenAsync(registration, UnixTimeUtc.Now());
    }

    //

    public Task LastSeenNowAsync(TenantContext tenantContext)
    {
        return PutLastSeenAsync(tenantContext, UnixTimeUtc.Now());
    }

    //

    public Task LastSeenNowAsync(OdinIdentity odinIdentity)
    {
        return PutLastSeenAsync(odinIdentity, UnixTimeUtc.Now());
    }


    //

    public Task LastSeenNowAsync(Guid identityId)
    {
        return PutLastSeenAsync(identityId, UnixTimeUtc.Now());
    }

    //

    public Task PutLastSeenAsync(IdentityRegistration registration, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(registration.Id, lastSeen);
    }

    //

    public Task PutLastSeenAsync(TenantContext tenantContext, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(tenantContext.DotYouRegistryId, lastSeen);
    }

    //

    public Task PutLastSeenAsync(OdinIdentity odinIdentity, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(odinIdentity.IdentityId, lastSeen);
    }

    //

    public Task PutLastSeenAsync(LastSeenRecord record)
    {
        return PutLastSeenAsync(record.identityId, record.timestamp);
    }

    //

    public async Task PutLastSeenAsync(Guid identityId, UnixTimeUtc lastSeen)
    {
        if (_lastSeenByIdentityId.TryGetValue(identityId, out var lastSeenCached))
        {
            var diff = TimeSpan.FromMilliseconds(lastSeen.milliseconds - lastSeenCached.milliseconds);
            if (diff < UpdateThreshold)
            {
                // Don't thrash the cache with updates that are too close to each other
                return;
            }
        }

        _lastSeenByIdentityId[identityId] = lastSeen;

        await cache.SetAsync(BuildCacheKey(identityId), lastSeen, LastSeenEntryTtl);
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId)
    {
        var result = await cache.GetOrSetAsync(
            BuildCacheKey(identityId),
            async _ =>
            {
                await using var scope = rootScope.BeginLifetimeScope();
                var lastSeen = scope.Resolve<TableLastSeen>();
                return await lastSeen.GetLastSeenAsync(identityId);
            },
            LastSeenEntryTtl);

        if (result == null)
        {
            return null;
        }

        _lastSeenByIdentityId[identityId] = result.Value;
        return result;
    }

    //

    internal async Task UpdateDatabaseAsync()
    {
        if (_lastSeenByIdentityId.IsEmpty)
        {
            return;
        }

        var updates = _lastSeenByIdentityId.ToDictionary();
        _lastSeenByIdentityId.Clear();

        await using var scope = rootScope.BeginLifetimeScope();
        var lastSeen = scope.Resolve<TableLastSeen>();
        await lastSeen.UpdateLastSeenAsync(updates);
    }

    //

}
