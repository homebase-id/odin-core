using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableCircleCached(
    TableCircle table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
{
    private static readonly List<string> PagingByCircleIdTags = ["PagingByCircleId"];

    //

    private static string GetCacheKey(CircleRecord item)
    {
        return GetCacheKey(item.circleId);
    }

    //

    private static string GetCacheKey(Guid circleId)
    {
        return circleId.ToString();
    }

    //

    private Task InvalidateAsync(CircleRecord item)
    {
        return InvalidateAsync(item.circleId);
    }

    //

    private Task InvalidateAsync(Guid circleId)
    {
        return InvalidateAsync([
            () => InvalidateAsync(GetCacheKey(circleId)),
            () => InvalidateByTagAsync(PagingByCircleIdTags)
        ]);
    }

    //

    public async Task<CircleRecord?> GetAsync(Guid circleId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetCacheKey(circleId),
            _ => table.GetAsync(circleId),
            ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(CircleRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;

    }

    //

    public async Task<int> DeleteAsync(Guid circleId)
    {
        var result = await table.DeleteAsync(circleId);

        await InvalidateAsync(circleId);

        return result;
    }

    //

    public async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(
        int count,
        Guid? inCursor,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + inCursor,
            _ => table.PagingByCircleIdAsync(count, inCursor),
            ttl,
            PagingByCircleIdTags);

        return result;
    }

    //

}