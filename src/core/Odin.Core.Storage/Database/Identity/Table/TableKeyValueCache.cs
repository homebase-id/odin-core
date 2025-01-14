using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyValueCache(
    IGenericMemoryCache<TableKeyValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableKeyValue table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<KeyValueRecord?> GetAsync(byte[] key, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        if (cache.TryGet<KeyValueRecord?>(key, out var record))
        {
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

        cache.Remove(key);
        var affectedRows = await table.DeleteAsync(key);

        return affectedRows;
    }

    //
}
