using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppNotificationsCache(
    IGenericMemoryCache<TableAppNotificationsCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableAppNotifications table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<AppNotificationsRecord> GetAsync(Guid notificationId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = notificationId.ToString();

        if (cache.TryGet<AppNotificationsRecord>(cacheKey, out var record))
        {
            return record;
        }

        record = await table.GetAsync(notificationId);
        cache.Set(cacheKey, record, options);
        return record;
    }

    //

    public async Task<int> InsertAsync(AppNotificationsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.notificationId.ToString();

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(AppNotificationsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.notificationId.ToString();

        var affectedRows = await table.UpdateAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(Guid notificationId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = notificationId.ToString();
        cache.Remove(cacheKey);

        return await table.DeleteAsync(notificationId);
    }

    //

}