using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyThreeValueCached(TableKeyThreeValue table, IIdentityTransactionalCacheFactory cacheFactory) :
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
        var result = await Cache.GetOrSetAsync(GetCacheKey1(key1), _ => table.GetAsync(key1), ttl);
        return result;
    }

    //

    public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey2(key2), _ => table.GetByKeyTwoAsync(key2), ttl);
        return result;
    }

    //

    public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey3(key3), _ => table.GetByKeyThreeAsync(key3), ttl);
        return result;
    }

    //

    public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey23(key2, key3), _ => table.GetByKeyTwoThreeAsync(key2, key3), ttl);
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyThreeValueRecord item)
    {
        var result = await table.UpsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyThreeValueRecord item)
    {
        var result = await table.InsertAsync(item);
        await Cache.InvalidateAllAsync();
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

    public async Task<int> UpdateAsync(KeyThreeValueRecord item)
    {
        var result = await table.UpdateAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

}
