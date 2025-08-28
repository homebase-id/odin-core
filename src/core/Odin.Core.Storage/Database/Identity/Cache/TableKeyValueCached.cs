using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableKeyValueCached(
    TableKeyValue table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
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

    private Task InvalidateAsync(KeyValueRecord item)
    {
        return InvalidateAsync(item.key);
    }

    //

    private async Task InvalidateAsync(List<KeyValueRecord> items)
    {
        var keys = items.Select(i => i.key).Distinct();
        var actions = keys.Select(key => CreateRemoveByKeyAction(GetCacheKey(key))).ToList();
        await InvalidateAsync(actions);
    }

    //

    private async Task InvalidateAsync(byte[] key)
    {
        await InvalidateAsync([
            CreateRemoveByKeyAction(GetCacheKey(key)),
        ]);
    }

    //

    public async Task<KeyValueRecord?> GetAsync(byte[] key, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(GetCacheKey(key), _ => table.GetAsync(key), ttl);
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
