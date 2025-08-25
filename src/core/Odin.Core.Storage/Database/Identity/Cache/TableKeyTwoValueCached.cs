using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableKeyTwoValueCached(TableKeyTwoValue table, ITenantLevel2Cache cache) : AbstractTableCaching(cache)
{
    private static string GetCacheKey(KeyTwoValueRecord item)
    {
        return GetCacheKey1(item.key1);
    }

    //

    private static string GetCacheKey1(byte[] key1)
    {
        return "key1" + ":" + key1.ToHexString();
    }

    //

    private static string GetCacheKey2(byte[] key2)
    {
        return "key2" + ":" + key2.ToHexString();
    }

    //

    public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey2(key2), _ => table.GetByKeyTwoAsync(key2), ttl);
        return result;
    }

    //

    public async Task<KeyTwoValueRecord?> GetAsync(byte[] key1, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey1(key1), _ => table.GetAsync(key1), ttl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(byte[] key1)
    {
        var result = await table.DeleteAsync(key1);
        await InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyTwoValueRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyTwoValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> UpdateAsync(KeyTwoValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpdateAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

}
