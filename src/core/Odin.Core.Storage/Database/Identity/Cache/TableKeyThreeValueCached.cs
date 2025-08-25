using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableKeyThreeValueCached(TableKeyThreeValue table, ITenantLevel2Cache cache) : AbstractTableCaching(cache)
{
    private static string GetCacheKey(KeyThreeValueRecord item)
    {
        return GetCacheKey1(item.key1);
    }

    //

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

    private static string GetCacheKey3(byte[] key3)
    {
        return "key3:" + key3.ToHexString();
    }

    //

    private static string GetCacheKey23(byte[] key2, byte[] key3)
    {
        return "key2:" + key2.ToHexString() + ":key3:" + key3.ToHexString();
    }

    //

    public async Task<KeyThreeValueRecord?> GetAsync(byte[] key1, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey1(key1), _ => table.GetAsync(key1), ttl);
        return result;
    }

    //

    public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey2(key2), _ => table.GetByKeyTwoAsync(key2), ttl);
        return result;
    }

    //

    public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey3(key3), _ => table.GetByKeyThreeAsync(key3), ttl);
        return result;
    }

    //

    public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey23(key2, key3), _ => table.GetByKeyTwoThreeAsync(key2, key3), ttl);
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyThreeValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyThreeValueRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
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

    public async Task<int> UpdateAsync(KeyThreeValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpdateAsync(item);
        await InvalidateAllAsync();
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

}
