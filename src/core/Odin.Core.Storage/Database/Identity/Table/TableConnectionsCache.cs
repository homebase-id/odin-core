using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableConnectionsCache(
    IGenericMemoryCache<TableConnectionsCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableConnections table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<ConnectionsRecord?> GetAsync(OdinId identity, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = identity.DomainName;

        if (cache.TryGet<ConnectionsRecord?>(cacheKey, out var record))
        {
            return record;
        }

        record = await table.GetAsync(identity);
        cache.Set(cacheKey, record, options);
        return record;
    }

    //

    public async Task<int> InsertAsync(ConnectionsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.identity.DomainName;

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpdateAsync(ConnectionsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.identity.DomainName;

        var affectedRows = await table.UpdateAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(ConnectionsRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = record.identity.DomainName;

        var affectedRows = await table.UpsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity)
    {
        NoTransactionCheck();

        var cacheKey = identity.DomainName;

        cache.Remove(cacheKey);
        var affectedRows = await table.DeleteAsync(identity);

        return affectedRows;
    }

    //

}