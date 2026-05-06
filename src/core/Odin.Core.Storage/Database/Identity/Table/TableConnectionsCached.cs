using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public sealed record ConnectionsPage(List<ConnectionsRecord> Records, string NextCursor);

public class TableConnectionsCached(TableConnections table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    private static readonly List<string> PagingByTags = ["PagingBy"];

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
        await Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(identity)),
            Cache.CreateRemoveByTagsAction(PagingByTags)
        ]);
    }

    //

    public async Task<ConnectionsRecord?> GetAsync(OdinId identity, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(identity),
            _ => table.GetAsync(identity),
            ttl ?? DefaultTtl);
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

    public async Task<ConnectionsPage> PagingByIdentityAsync(
        int count,
        string? inCursor,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByIdentity" + ":" + count + ":" + inCursor,
            async _ =>
            {
                var (records, nextCursor) = await table.PagingByIdentityAsync(count, inCursor);
                return new ConnectionsPage(records, nextCursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByTags);
    }

    //

    public async Task<ConnectionsPage> PagingByIdentityAsync(
        int count,
        int status,
        string? inCursor,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByIdentity" + ":" + count + ":" + status + ":" + inCursor,
            async _ =>
            {
                var (records, nextCursor) = await table.PagingByIdentityAsync(count, status, inCursor);
                return new ConnectionsPage(records, nextCursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByTags);
    }

    //

    public async Task<ConnectionsPage> PagingByCreatedAsync(
        int count,
        int status,
        string? cursorString,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + status + ":" + cursorString,
            async _ =>
            {
                var (records, nextCursor) = await table.PagingByCreatedAsync(count, status, cursorString);
                return new ConnectionsPage(records, nextCursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByTags);
    }

    //

    public async Task<ConnectionsPage> PagingByCreatedAsync(
        int count,
        string? cursorString,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + cursorString,
            async _ =>
            {
                var (records, nextCursor) = await table.PagingByCreatedAsync(count, cursorString);
                return new ConnectionsPage(records, nextCursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByTags);
    }

    //

}
