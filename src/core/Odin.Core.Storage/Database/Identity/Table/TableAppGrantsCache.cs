using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableAppGrantsCache(
    IGenericMemoryCache<TableAppGrantsCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableAppGrants table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<bool> TryInsertAsync(AppGrantsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.odinHashId.ToString(), record.odinHashId.ToString(), record.circleId.ToString());

        var inserted = await table.TryInsertAsync(record);
        cache.Set(cacheKey, record, options);

        return inserted;
    }

    //

    public async Task<int> InsertAsync(AppGrantsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.odinHashId.ToString(), record.odinHashId.ToString(), record.circleId.ToString());

        var rowsAffected = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return rowsAffected;
    }

    //

    public async Task<int> UpsertAsync(AppGrantsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.odinHashId.ToString(), record.odinHashId.ToString(), record.circleId.ToString());

        var rowsAffected = await table.UpsertAsync(record);
        cache.Set(cacheKey, record, options);

        return rowsAffected;

        // SEB:TODO clear other collections (e.g. GetByOdinHashIdAsync) that may be affected by this upsert
    }

    //

    public async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = odinHashId.ToString();

        if (cache.TryGet<List<AppGrantsRecord>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetByOdinHashIdAsync(odinHashId);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task DeleteByIdentityAsync(Guid odinHashId)
    {
        NoTransactionCheck();

        var cacheKey = odinHashId.ToString();

        cache.Remove(cacheKey);
        await table.DeleteByIdentityAsync(odinHashId);
    }


}