using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableCircleMemberCache(
    IGenericMemoryCache<TableCircleMemberCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    TableCircleMember table)
    : AbstractTableCache(cache, scopedConnectionFactory)
{
    public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(circleId.ToString(), memberId.ToString());
        cache.Remove(cacheKey);
        return await table.DeleteAsync(circleId, memberId);
    }

    //

    public async Task<int> InsertAsync(CircleMemberRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.circleId.ToString(), record.memberId.ToString());

        var affectedRows = await table.InsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<int> UpsertAsync(CircleMemberRecord record, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(record.circleId.ToString(), record.memberId.ToString());

        var affectedRows = await table.UpsertAsync(record);
        cache.Set(cacheKey, record, options);

        return affectedRows;
    }

    //

    public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(nameof(GetCircleMembersAsync), circleId.ToString());

        if (cache.TryGet<List<CircleMemberRecord>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetCircleMembersAsync(circleId);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

    public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid circleId, MemoryCacheEntryOptions options)
    {
        NoTransactionCheck();

        var cacheKey = cache.GenerateKey(nameof(GetMemberCirclesAndDataAsync), circleId.ToString());

        if (cache.TryGet<List<CircleMemberRecord>>(cacheKey, out var records))
        {
            return records ?? [];
        }

        records = await table.GetMemberCirclesAndDataAsync(circleId);
        cache.Set(cacheKey, records, options);
        return records ?? [];
    }

    //

}