using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyValueCache(
    ITenantLevel1Cache<TableKeyValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyValue table)
{
    public async Task ClearAsync()
    {
        await cache.RemoveByTagAsync(CacheTag);
    }

    //

    public async Task ClearAsync(byte[] key)
    {
        var hexKey = CacheKey(key);
        await cache.RemoveAsync(hexKey);
    }

    //

    public async Task<KeyValueRecord?> GetAsync(byte[] key, TimeSpan duration)
    {
        NoTransactionCheck();

        var hexKey = CacheKey(key);
        var record = await cache.GetOrSetAsync<KeyValueRecord?>(
            hexKey,
            _ => table.GetAsync(key),
            duration,
            CacheTag
        );

        return record;
    }

    //

    public async Task<int> InsertAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.InsertAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration, CacheTag);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpdateAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration, CacheTag);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpsertAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration, CacheTag);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(byte[] key)
    {
        NoTransactionCheck();

        var affectedRows = await table.DeleteAsync(key);

        var hexKey = CacheKey(key);
        await cache.RemoveAsync(hexKey);

        return affectedRows;
    }

    //

    public static string CacheKey(byte[] key) => key.ToHexString();
    private List<string> CacheTag => [GetType().Name];

    //

    private void NoTransactionCheck()
    {
        if (scopedConnectionFactory.HasTransaction)
        {
            throw new OdinSystemException("Must not access cache while in a transaction.");
        }
    }

    //

}
