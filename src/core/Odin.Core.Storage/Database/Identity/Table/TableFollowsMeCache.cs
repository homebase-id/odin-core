using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableFollowsMeCache(
    IGenericMemoryCache<TableFollowsMeCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableFollowsMe table)
    : AbstractTableCache(cache, scopedConnectionFactory)

{
    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(identity, driveId.ToString());

        cache.Remove(cacheKey);
        return await table.DeleteAsync(identity, driveId);
    }

    //

    public async Task<int> DeleteAndInsertManyAsync(OdinId identity, List<FollowsMeRecord> records, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        cache.Clear();

        var affectedRows = await table.DeleteAndInsertManyAsync(identity, records);

        foreach (var record in records)
        {
            var cacheKey = cache.GenerateKey(identity, record.driveId.ToString());
            cache.Set(cacheKey, record, options);
        }

        return affectedRows;
    }

    //

    public async Task<int> InsertAsync(FollowsMeRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.identity, record.driveId.ToString());

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<List<FollowsMeRecord>> GetAsync(OdinId identity, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = identity.ToString();

        if (cache.TryGet<List<FollowsMeRecord>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetAsync(identity);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task<int> DeleteByIdentityAsync(OdinId identity)
    {
        NoTransactionCheck();

        cache.Clear();

        return await table.DeleteByIdentityAsync(identity);
    }

    //
}