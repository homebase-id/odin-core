using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableMySubscriptionsCached(TableMySubscriptions table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, RootInvalidationTag)
{
    public const string RootInvalidationTag = nameof(TableMySubscriptions);

    //

    private string GetSubscriptionCacheKey(OdinId sourceOwnerOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return $"sourceOwner:{sourceOwnerOdinId}:sourceDrive:{sourceDriveId}:sourceType:{sourceDriveTypeId}:target:{targetDriveId}";
    }

    //

    private string GetAllCacheKey()
    {
        return "all";
    }

    //

    public async Task<MySubscriptionsRecord?> GetAsync(OdinId sourceOwnerOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetSubscriptionCacheKey(sourceOwnerOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId),
            _ => table.GetAsync(sourceOwnerOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<MySubscriptionsRecord>> GetAllAsync(TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetAllCacheKey(),
            _ => table.GetAllAsync(),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> DeleteAsync(OdinId sourceOwnerOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        var result = await table.DeleteAsync(sourceOwnerOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> UpsertAsync(MySubscriptionsRecord item)
    {
        var result = await table.UpsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //
}