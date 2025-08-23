using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableImFollowingCached(TableImFollowing table, ITenantLevel2Cache cache) : AbstractTableCaching(cache)
{
    // SEB:NOTE some advanced queries are used in this table, so instead of trying to remove specific keys,
    // we use InvalidateAllAsync() to clear the cache whenever data changes are made

    //

    private static string GetCacheKey(ImFollowingRecord item)
    {
        return GetCacheKey(item.identity.DomainName, item.driveId);
    }

    //

    private static string GetCacheKey(string domainName, Guid driveId)
    {
        return domainName + ":" + driveId;
    }

    //

    private static string GetCacheKey(OdinId identity)
    {
        return identity.DomainName;
    }

    //

    public async Task<int> InsertAsync(ImFollowingRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        var result = await table.DeleteAsync(identity, driveId);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<List<ImFollowingRecord>> GetAsync(OdinId identity, TimeSpan ttl)
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
