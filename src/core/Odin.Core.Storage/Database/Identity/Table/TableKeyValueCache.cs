using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyValueCache(
    ILevel1Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyValue table)
{
    public void Clear()
    {
        cache.Clear();
    }

    //

    public void Clear(byte[] key)
    {
        var hexKey = CacheKey(key);
        cache.Remove(hexKey);
    }

    //

    public async Task<KeyValueRecord?> GetAsync(byte[] key, TimeSpan duration)
    {
        NoTransactionCheck();

        var hexKey = CacheKey(key);
        var record = await cache.GetOrSetAsync<KeyValueRecord?>(
            hexKey,
            _ => table.GetAsync(key),
            duration
        );

        return record;
    }

    //

    public async Task<int> InsertAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.InsertAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpdateAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(KeyValueRecord record, TimeSpan duration)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpsertAsync(record);
        var hexKey = CacheKey(record.key);
        await cache.SetAsync(hexKey, record, duration);

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

    public string CacheKey(byte[] key) => GetType().Name + ":" + key.ToHexString();

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
