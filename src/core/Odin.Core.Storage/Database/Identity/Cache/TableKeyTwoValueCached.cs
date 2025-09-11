using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableKeyTwoValueCached(TableKeyTwoValue table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    // SEB:NOTE some funky cache keys here. We'll invalidate everything on any change. Might be worth refining later.

    private static string GetCacheKey1(byte[] key1)
    {
        return "key1:" + key1.ToHexString();
    }

    //

    private static string GetCacheKey2(byte[] key2)
    {
        return "key2:" + key2.ToHexString();
    }

    //

    public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey2(key2), _ => table.GetByKeyTwoAsync(key2), ttl);
        return result;
    }

    //

    public async Task<KeyTwoValueRecord?> GetAsync(byte[] key1, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey1(key1), _ => table.GetAsync(key1), ttl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(byte[] key1)
    {
        var result = await table.DeleteAsync(key1);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyTwoValueRecord item)
    {
        var result = await table.InsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyTwoValueRecord item)
    {
        var result = await table.UpsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> UpdateAsync(KeyTwoValueRecord item)
    {
        var result = await table.UpdateAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

}
