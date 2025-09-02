using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;

[assembly: InternalsVisibleTo("Odin.Services.Tests")]

namespace Odin.Services.Registry.LastSeen;

#nullable enable

public class LastSeenService(
    ISystemLevel2Cache<LastSeenService> cache,
    TableRegistrations registrations) : ILastSeenService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

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

    public Task PutLastSeenAsync(RegistrationsRecord? record)
    {
        return record?.lastSeen != null
            ? PutLastSeenAsync(record.identityId, record.primaryDomainName, record.lastSeen.Value)
            : Task.CompletedTask;
    }

    //

    public async Task PutLastSeenAsync(Guid identityId, string domain, UnixTimeUtc lastSeen)
    {
        var lastSeenEntry = new LastSeenEntry(identityId, domain, lastSeen);

        _lastSeenByIdentityId[identityId] = lastSeenEntry;

        await Task.WhenAll(
            cache.SetAsync(BuildCacheKey(identityId), lastSeenEntry, Ttl).AsTask(),
            cache.SetAsync(BuildCacheKey(domain), lastSeenEntry, Ttl).AsTask());
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId)
    {
        var result = await cache.GetOrSetAsync(
            BuildCacheKey(identityId),
            _ => registrations.GetLastSeenAsync(identityId),
            Ttl);

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
            _ => registrations.GetLastSeenAsync(domain),
            Ttl);

        if (result == null)
        {
            return null;
        }

        _lastSeenByIdentityId[result.IdentityId] = result;
        return result.LastSeen;
    }

    //

    internal async Task UpdateDatabaseAsync()
    {
        if (_lastSeenByIdentityId.IsEmpty)
        {
            return;
        }

        var toUpdate = _lastSeenByIdentityId.ToDictionary(kv => kv.Key, kv => kv.Value);
        _lastSeenByIdentityId.Clear();

        await registrations.UpdateLastSeenAsync(toUpdate);
    }

    //

}
