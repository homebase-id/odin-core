using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableImFollowingCached(TableImFollowing table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    // SEB:NOTE some advanced queries are used in this table, so instead of trying to remove specific keys,
    // we use Cache.InvalidateAllAsync() to clear the cache whenever data changes are made

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

    public async Task<int> InsertAsync(ImFollowingRecord item)
    {
        var result = await table.InsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        var result = await table.DeleteAsync(identity, driveId);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<List<ImFollowingRecord>> GetAsync(OdinId identity, TimeSpan ttl)
    {
        var result =  await Cache.GetOrSetAsync(
            GetCacheKey(identity),
            _ => table.GetAsync(identity),
            ttl);
        return result;
    }

    //

    public async Task<int> DeleteByIdentityAsync(OdinId identity)
    {
        var result = await table.DeleteByIdentityAsync(identity);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<(List<string> followers, string nextCursor)> GetAllFollowersAsync(
        int count,
        string? inCursor,
        TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
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
        var result = await Cache.GetOrSetAsync(
            "GetFollowers" + ":" + count + ":" + driveId + ":" + inCursor,
            _ => table.GetFollowersAsync(count, driveId, inCursor),
            ttl);
        return result;
    }

    //

}
