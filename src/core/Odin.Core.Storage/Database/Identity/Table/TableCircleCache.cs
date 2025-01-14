using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableCircleCache(
    IGenericMemoryCache<TableCircleCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableCircle table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<CircleRecord?> GetAsync(Guid circleId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = circleId.ToString();

        if (cache.TryGet<CircleRecord>(cacheKey, out var record))
        {
            return record;
        }

        record = await table.GetAsync(circleId);
        cache.Set(cacheKey, record, options);
        return record;
    }

    //

    public async Task<int> InsertAsync(CircleRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.circleId.ToString();

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(Guid circleId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = circleId.ToString();
        cache.Remove(cacheKey);

        return await table.DeleteAsync(circleId);
    }

    //

}