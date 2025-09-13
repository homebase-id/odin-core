using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyValueCached(TableKeyValue table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    //

    private static string GetCacheKey(byte[] key)
    {
        return key.ToHexString();
    }

    //

    private async Task InvalidateAsync(List<KeyValueRecord> items)
    {
        var keys = items.Select(i => i.key).Distinct();
        var actions = keys.Select(key => Cache.CreateRemoveByKeyAction(GetCacheKey(key))).ToList();
        await Cache.InvalidateAsync(actions);
    }

    //

    private async Task InvalidateAsync(byte[] key)
    {
        await Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(key)),
        ]);
    }

    //

    public async Task<KeyValueRecord?> GetAsync(byte[] key, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(GetCacheKey(key), _ => table.GetAsync(key), ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(KeyValueRecord item)
    {
        var result = await table.InsertAsync(item);
        await InvalidateAsync(item.key);
        return result;
    }

    //

    public async Task<bool> TryInsertAsync(KeyValueRecord item)
    {
        var result = await table.TryInsertAsync(item);
        if (result)
        {
            await InvalidateAsync(item.key);
        }
        return result;
    }

    //

    public async Task<int> UpsertAsync(KeyValueRecord item)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAsync(item.key);
        return result;
    }

    //

    public async Task<int> UpdateAsync(KeyValueRecord item)
    {
        var result = await table.UpdateAsync(item);
        await InvalidateAsync(item.key);
        return result;
    }

    //

    public async Task<int> DeleteAsync(byte[] key)
    {
        var result = await table.DeleteAsync(key);
        await InvalidateAsync(key);
        return result;
    }

    //

    public async Task<int> UpsertManyAsync(List<KeyValueRecord> items)
    {
        var result = await table.UpsertManyAsync(items);
        await InvalidateAsync(items);
        return result;
    }

    //

}
