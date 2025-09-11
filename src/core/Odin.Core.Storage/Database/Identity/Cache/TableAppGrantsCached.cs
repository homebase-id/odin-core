using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableAppGrantsCached(TableAppGrants table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name)
{
    private const string CacheKeyAll = "all:all:all";

    //

    private static string GetCacheKey(AppGrantsRecord item)
    {
        return GetCacheKey(item.odinHashId, item.appId, item.circleId);
    }

    //

    private static string GetCacheKey(Guid? odinHashId, Guid? appId, Guid? circleId)
    {
        return odinHashId + ":" + appId + ":" + circleId;
    }

    //

    private Task InvalidateAsync(AppGrantsRecord item)
    {
        return InvalidateAsync(item.odinHashId, item.appId, item.circleId);
    }

    //

    private Task InvalidateAsync(Guid? odinHashId, Guid? appId, Guid? circleId)
    {
        return Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(odinHashId, null, null)),
            Cache.CreateRemoveByKeyAction(GetCacheKey(odinHashId, appId, circleId)),
            Cache.CreateRemoveByKeyAction(CacheKeyAll),
        ]);
    }

    //

    public async Task<int> InsertAsync(AppGrantsRecord item)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<bool> TryInsertAsync(AppGrantsRecord item)
    {
        var result = await table.TryInsertAsync(item);
        if (result)
        {
            await InvalidateAsync(item);
        }
        return result;
    }

    //

    public async Task<int> UpsertAsync(AppGrantsRecord item)
    {
        var result = await table.UpsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(odinHashId, null, null),
            _ => table.GetByOdinHashIdAsync(odinHashId),
            ttl);
        return result;
    }

    //

    public async Task<List<AppGrantsRecord>> GetAllAsync(TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            CacheKeyAll,
            _ => table.GetAllAsync(),
            ttl);
        return result;
    }

    //

    public async Task DeleteByIdentityAsync(Guid odinHashId)
    {
        await table.DeleteByIdentityAsync(odinHashId);
        // There's no easy way to invalidate all records for a specific identity,
        // so we'll have to invalidate all records instead.
        await Cache.InvalidateAllAsync();
    }

    //

}
