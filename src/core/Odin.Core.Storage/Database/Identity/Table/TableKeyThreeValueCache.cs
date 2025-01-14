using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyThreeValueCache(
    IGenericMemoryCache<TableKeyThreeValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyThreeValue table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<KeyThreeValueRecord?> GetAsync(byte[] key1, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key1", key1.ToBase64());

        if (cache.TryGet<KeyThreeValueRecord?>(cacheKey, out var record))
        {
            return record;
        }

        record = await table.GetAsync(key1);
        cache.Set(cacheKey, record, options);
        return record;
    }

    //

    public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key2", key2.ToBase64());

        if (cache.TryGet<List<byte[]>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetByKeyTwoAsync(key2);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key3", key3.ToBase64());

        if (cache.TryGet<List<byte[]>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetByKeyTwoAsync(key3);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key2and3", key2.ToBase64(), key3.ToBase64());

        if (cache.TryGet<List<KeyThreeValueRecord>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetByKeyTwoThreeAsync(key2, key3);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task<int> UpsertAsync(KeyThreeValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key1", record.key1.ToBase64());

        var affectedRows = await table.UpsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> InsertAsync(KeyThreeValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key1", record.key1.ToBase64());

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(byte[] key1, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key1", key1.ToBase64());

        cache.Remove(cacheKey);
        var affectedRows = await table.DeleteAsync(key1);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(KeyThreeValueRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey("key1", record.key1.ToBase64());

        var affectedRows = await table.UpdateAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

}