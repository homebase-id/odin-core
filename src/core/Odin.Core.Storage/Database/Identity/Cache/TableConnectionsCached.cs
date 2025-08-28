using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableConnectionsCached(
    TableConnections table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
{
    private static readonly List<string> PagingByTags = ["PagingBy"];

    //

    private static string GetCacheKey(ConnectionsRecord item)
    {
        return GetCacheKey(item.identity);
    }

    //

    private static string GetCacheKey(OdinId identity)
    {
        return identity.DomainName;
    }

    //

    private Task InvalidateAsync(ConnectionsRecord item)
    {
        return InvalidateAsync(item.identity);
    }

    //

    private async Task InvalidateAsync(OdinId identity)
    {
        await InvalidateAsync([
            CreateRemoveByKeyAction(GetCacheKey(identity)),
            CreateRemoveByTagAction(PagingByTags)
        ]);
    }

    //

    public async Task<ConnectionsRecord?> GetAsync(OdinId identity, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetCacheKey(identity),
            _ => table.GetAsync(identity),
            ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(ConnectionsRecord item)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAsync(item);
        return result;
    }

    //

    public async Task<int> UpsertAsync(ConnectionsRecord item)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAsync(item);
        return result;
    }

    //

    public async Task<int> UpdateAsync(ConnectionsRecord item)
    {
        var result = await table.UpdateAsync(item);
        await InvalidateAsync(item);
        return result;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity)
    {
        var result = await table.DeleteAsync(identity);
        await InvalidateAsync(identity);
        return result;
    }

    //

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(
        int count,
        string? inCursor,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByIdentity" + ":" + count + ":" + inCursor,
            _ => table.PagingByIdentityAsync(count, inCursor),
            ttl,
            PagingByTags);
        return result;
    }

    //

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(
        int count,
        int status,
        string? inCursor,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByIdentity" + ":" + count + ":" + status + ":" + inCursor,
            _ => table.PagingByIdentityAsync(count, status, inCursor),
            ttl,
            PagingByTags);
        return result;
    }

    //

    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(
        int count,
        int status,
        string? cursorString,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + status + ":" + cursorString,
            _ => table.PagingByCreatedAsync(count, status, cursorString),
            ttl,
            PagingByTags);
        return result;
    }

    //

    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(
        int count,
        string? cursorString,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + cursorString,
            _ => table.PagingByCreatedAsync(count, cursorString),
            ttl,
            PagingByTags);
        return result;
    }

    //

}
