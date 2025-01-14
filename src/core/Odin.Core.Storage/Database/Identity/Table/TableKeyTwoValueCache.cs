using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyTwoValueCache(
    IGenericMemoryCache<TableKeyTwoValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyTwoValue table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    // SEB:TODO unit test this
    public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        if (cache.TryGet<List<KeyTwoValueRecord>>(key2, out var records))
        {
            return records ?? [];
        }

        records = await table.GetByKeyTwoAsync(key2);
        cache.Set(key2, records, options);
        return records ?? [];
    }

    //

    public async Task<KeyTwoValueRecord?> GetAsync(byte[] key1, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        if (cache.TryGet<KeyTwoValueRecord?>(key1, out var record))
        {
            return record;
        }

        record = await table.GetAsync(key1);
        cache.Set(key1, record, options);
        return record;
    }

    //

    public async Task<int> DeleteAsync(byte[] key1)
    {
        NoTransactionCheck();

        cache.Remove(key1);
        var affectedRows = await table.DeleteAsync(key1);

        return affectedRows;
    }

    //

    public async Task<int> InsertAsync(KeyTwoValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.InsertAsync(record);
        cache.Set(record.key1, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(KeyTwoValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.InsertAsync(record);
        cache.Set(record.key1, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(KeyTwoValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var affectedRows = await table.UpdateAsync(record);
        cache.Set(record.key1, record, options);

        return affectedRows;
    }

    //

}
