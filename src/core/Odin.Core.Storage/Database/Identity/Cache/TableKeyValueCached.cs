using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableKeyValueCached(TableKeyValue table, ITenantLevel2Cache cache) : AbstractTableCaching(cache)
{
    private static string GetCacheKey(KeyValueRecord item)
    {
        return GetCacheKey(item.key);
    }

    //

    private static string GetCacheKey(byte[] key)
    {
        return key.ToHexString();
    }

    //

    public async Task<KeyValueRecord?> GetAsync(byte[] key, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey(key), _ => table.GetAsync(key), ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyValueRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<bool> TryInsertAsync(KeyValueRecord item, TimeSpan ttl)
    {
        var result = await table.TryInsertAsync(item);
        if (result)
        {
            await SetAsync(GetCacheKey(item), item, ttl);
        }
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpsertAsync(item);
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> UpdateAsync(KeyValueRecord item, TimeSpan ttl)
    {
        var result = await table.UpdateAsync(item);
        await SetAsync(GetCacheKey(item), item, ttl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(byte[] key)
    {
        var result = await table.DeleteAsync(key);
        await RemoveAsync(GetCacheKey(key));
        return result;
    }

    //

}
