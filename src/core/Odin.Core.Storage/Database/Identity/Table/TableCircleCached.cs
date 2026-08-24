using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public sealed record CirclePage(List<CircleRecord> Records, Guid? NextCursor);

public class TableCircleCached(TableCircle table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    private static readonly List<string> PagingByCircleIdTags = ["PagingByCircleId"];
    private const string CacheKeyAll = "GetAll";
    private static readonly List<string> ByGrantOnTags = ["ByGrantOn"];

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
        return Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(circleId)),
            Cache.CreateRemoveByKeyAction(CacheKeyAll),
            Cache.CreateRemoveByTagsAction(ByGrantOnTags),
            Cache.CreateRemoveByTagsAction(PagingByCircleIdTags)
        ]);
    }

    //

    public async Task<CircleRecord?> GetAsync(Guid circleId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(circleId),
            _ => table.GetAsync(circleId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> InsertAsync(CircleRecord item)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;

    }

    //

    public async Task<int> UpsertAsync(CircleRecord item)
    {
        var result = await table.UpsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<List<CircleRecord>> GetAllAsync(TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetListAsync(
            CacheKeyAll,
            _ => table.GetAllAsync(),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<CircleRecord>> GetByGrantOnAsync(int grantOn, TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetListAsync(
            "ByGrantOn:" + grantOn,
            _ => table.GetByGrantOnAsync(grantOn),
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            ByGrantOnTags);
    }

    //

    public async Task<int> DeleteAsync(Guid circleId)
    {
        var result = await table.DeleteAsync(circleId);

        await InvalidateAsync(circleId);

        return result;
    }

    //

    public async Task<CirclePage> PagingByCircleIdAsync(
        int count,
        Guid? inCursor,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + inCursor,
            async _ =>
            {
                var (records, nextCursor) = await table.PagingByCircleIdAsync(count, inCursor);
                return new CirclePage(records, nextCursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByCircleIdTags);
    }

    //

}