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

[assembly: InternalsVisibleTo("Odin.Services.Tests")]

namespace Odin.Services.Registry.LastSeen;

#nullable enable

public class LastSeenService(ISystemLevel2Cache<LastSeenService> cache, ILifetimeScope rootScope) : ILastSeenService
{
    private static readonly TimeSpan LastSeenEntryTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UpdateThreshold = TimeSpan.FromSeconds(10);

    // SEB:NOTE we need this local lists to batch updates to the database
    // since the cache can't provide us with the list. Stupid cache.
    private readonly ConcurrentDictionary<Guid, LastSeenEntry> _lastSeenByIdentityId = new();

    //

    private static string BuildCacheKey(Guid identityId) => "last-seen:" + identityId;
    private static string BuildCacheKey(string domain) => "last-seen:" + domain;

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

    public Task LastSeenNowAsync(Guid identityId, string domain)
    {
        return PutLastSeenAsync(identityId, domain, UnixTimeUtc.Now());
    }

    //

    public Task PutLastSeenAsync(IdentityRegistration registration, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(registration.Id, registration.PrimaryDomainName, lastSeen);
    }

    //

    public Task PutLastSeenAsync(TenantContext tenantContext, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(tenantContext.DotYouRegistryId, tenantContext.HostOdinId, lastSeen);
    }

    //

    public Task PutLastSeenAsync(OdinIdentity odinIdentity, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(odinIdentity.IdentityId, odinIdentity.PrimaryDomain, lastSeen);
    }

    //

    public Task PutLastSeenAsync(RegistrationsRecord? record)
    {
        return record?.lastSeen != null
            ? PutLastSeenAsync(record.identityId, record.primaryDomainName, record.lastSeen.Value)
            : Task.CompletedTask;
    }

    //

    public async Task PutLastSeenAsync(Guid identityId, string domain, UnixTimeUtc lastSeen)
    {
        if (_lastSeenByIdentityId.TryGetValue(identityId, out var lastSeenEntry))
        {
            var diff = TimeSpan.FromMilliseconds(lastSeen.milliseconds - lastSeenEntry.LastSeen.milliseconds);
            if (diff < UpdateThreshold)
            {
                // Don't thrash the cache with updates that are too close to each other
                return;
            }
        }

        lastSeenEntry = new LastSeenEntry(identityId, domain, lastSeen);

        _lastSeenByIdentityId[identityId] = lastSeenEntry;

        await Task.WhenAll(
            cache.SetAsync(BuildCacheKey(identityId), lastSeenEntry, LastSeenEntryTtl).AsTask(),
            cache.SetAsync(BuildCacheKey(domain), lastSeenEntry, LastSeenEntryTtl).AsTask());
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId)
    {
        var result = await cache.GetOrSetAsync(
            BuildCacheKey(identityId),
            async _ =>
            {
                await using var scope = rootScope.BeginLifetimeScope();
                var registrations = scope.Resolve<TableRegistrations>();
                return await registrations.GetLastSeenAsync(identityId);
            },
            LastSeenEntryTtl);

        if (result == null)
        {
            return null;
        }

        _lastSeenByIdentityId[identityId] = result;
        return result.LastSeen;
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(string domain)
    {
        var result = await cache.GetOrSetAsync(
            BuildCacheKey(domain),
            async _ =>
            {
                await using var scope = rootScope.BeginLifetimeScope();
                var registrations = scope.Resolve<TableRegistrations>();
                return await registrations.GetLastSeenAsync(domain);
            },
            LastSeenEntryTtl);

        if (result == null)
        {
            return null;
        }

        _lastSeenByIdentityId[result.IdentityId] = result;
        return result.LastSeen;
    }

    public Task<UnixTimeUtc?> GetLastSeenAsync(IOdinContext odinContext)
    {
        return this.GetLastSeenAsync(odinContext.Tenant.DomainName);
    }

    //

    internal async Task UpdateDatabaseAsync()
    {
        if (_lastSeenByIdentityId.IsEmpty)
        {
            return;
        }

        await using var scope = rootScope.BeginLifetimeScope();
        var registrations = scope.Resolve<TableRegistrations>();

        var toUpdate = _lastSeenByIdentityId.ToDictionary(kv => kv.Key, kv => kv.Value);
        _lastSeenByIdentityId.Clear();

        await registrations.UpdateLastSeenAsync(toUpdate);
    }

    //

}
