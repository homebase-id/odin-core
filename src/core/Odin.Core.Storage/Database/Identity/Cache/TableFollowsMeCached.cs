using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableFollowsMeCached(
    TableFollowsMe table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
{
    // SEB:NOTE some advanced queries are used in this table, so instead of trying to remove specific keys,
    // we use InvalidateAllAsync() to clear the cache whenever data changes are made

    //

    private static string GetCacheKey(FollowsMeRecord item)
    {
        return GetCacheKey(item.identity, item.driveId);
    }

    //

    private static string GetCacheKey(string identity, Guid driveId)
    {
        return identity + ":" + driveId;
    }

    //

    private static string GetCacheKey(OdinId identity)
    {
        return identity.DomainName;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        var result = await table.DeleteAsync(identity, driveId);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> DeleteAndInsertManyAsync(OdinId identity, List<FollowsMeRecord> items)
    {
        var result = await table.DeleteAndInsertManyAsync(identity, items);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> InsertAsync(FollowsMeRecord item)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<bool> TryInsertAsync(FollowsMeRecord item)
    {
        var result = await table.TryInsertAsync(item);
        if (result)
        {
            await InvalidateAllAsync();
        }
        return result;
    }

    //

    public async Task<int> UpsertAsync(FollowsMeRecord item)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<List<FollowsMeRecord>> GetAsync(OdinId identity, TimeSpan ttl)
    {
        var result =  await GetOrSetAsync(
            GetCacheKey(identity),
            _ => table.GetAsync(identity),
            ttl);
        return result;
    }

    //

    public async Task<int> DeleteByIdentityAsync(OdinId identity)
    {
        var result = await table.DeleteByIdentityAsync(identity);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> DeleteAndAddFollowerAsync(FollowsMeRecord record)
    {
        var result = await table.DeleteAndAddFollowerAsync(record);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<(List<string> followers, string nextCursor)> GetAllFollowersAsync(
        int count,
        string? inCursor,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "GetAllFollowers" + ":" + count + ":" + inCursor,
            _ => table.GetAllFollowersAsync(count, inCursor),
            ttl);
        return result;
    }

    //

    public async Task<(List<string> followers, string nextCursor)> GetFollowersAsync(
        int count,
        Guid driveId,
        string? inCursor,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "GetFollowers" + ":" + count + ":" + driveId + ":" + inCursor,
            _ => table.GetFollowersAsync(count, driveId, inCursor),
            ttl);
        return result;
    }

    //

}
