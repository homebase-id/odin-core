using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableImFollowingCache(
    IGenericMemoryCache<TableImFollowingCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableImFollowing table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<int> InsertAsync(ImFollowingRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.identity.DomainName, record.driveId.ToString());

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(identity.DomainName, driveId.ToString());

        cache.Remove(cacheKey);
        return await table.DeleteAsync(identity, driveId);
    }

    //

    public async Task<List<ImFollowingRecord>> GetAsync(OdinId identity, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = identity.DomainName;

        if (cache.TryGet<List<ImFollowingRecord>>(cacheKey, out var records))
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

        var cacheKey = identity.DomainName;
        cache.Remove(cacheKey);

        return await table.DeleteByIdentityAsync(identity);
    }

    //

}