using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;
namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyValueCache(
    IGenericMemoryCache<TableKeyValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyValue table)
{
    public void Clear()
    {
        cache.Clear();
    }

    public void Clear(byte[] key)
    {
        cache.Remove(key);
    }

    public async Task<KeyValueRecord?> GetAsync(byte[] key, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        if (cache.TryGet<KeyValueRecord?>(key, out var record))
        {
            cache.Set(key, record, options);
            return record;
        }

        record = await table.GetAsync(key);
        cache.Set(key, record, options);
        return record;
    }

    //

    public async Task<int> InsertAsync(KeyValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.InsertAsync(record);
        cache.Set(record.key, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(KeyValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpdateAsync(record);
        cache.Set(record.key, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(KeyValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpsertAsync(record);
        cache.Set(record.key, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(byte[] key)
    {
        NoTransactionCheck();

        var affectedRows = await table.DeleteAsync(key);
        cache.Remove(key);

        return affectedRows;
    }

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
