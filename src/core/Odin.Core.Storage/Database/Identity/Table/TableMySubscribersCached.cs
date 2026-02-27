using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableMySubscribersCached(TableMySubscribers table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, RootInvalidationTag)
{
    public const string RootInvalidationTag = nameof(TableMySubscribers);

    //

    private string GetSubscriberCacheKey(OdinId subscriberOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return $"subscriber:{subscriberOdinId}:sourceDrive:{sourceDriveId}:sourceType:{sourceDriveTypeId}:target:{targetDriveId}";
    }

    //

    private string GetAllCacheKey()
    {
        return "all";
    }

    //

    public async Task<MySubscribersRecord?> GetAsync(OdinId subscriberOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetSubscriberCacheKey(subscriberOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId),
            _ => table.GetAsync(subscriberOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<MySubscribersRecord>> GetAllAsync(TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetAllCacheKey(),
            _ => table.GetAllAsync(),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(OdinId subscriberOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        var result = await table.DeleteAsync(subscriberOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> UpsertAsync(MySubscribersRecord item)
    {
        var result = await table.UpsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //
}