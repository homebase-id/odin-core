using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableAppNotificationsCached(TableAppNotifications table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    private static readonly List<string> PagingByCreateTags = ["PagingByCreate"];

    //

    private static string GetCacheKey(AppNotificationsRecord item)
    {
        return GetCacheKey(item.notificationId);
    }

    //

    private static string GetCacheKey(Guid notificationId)
    {
        return notificationId.ToString();
    }

    //

    private Task InvalidateAsync(AppNotificationsRecord item)
    {
        return InvalidateAsync(item.notificationId);
    }

    //

    private Task InvalidateAsync(Guid notificationId)
    {
        return Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(notificationId)),
            Cache.CreateRemoveByTagsAction(PagingByCreateTags)
        ]);
    }

    //

    public async Task<AppNotificationsRecord?> GetAsync(Guid notificationId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(notificationId),
            _ => table.GetAsync(notificationId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        var result = await table.UpdateAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(
        int count,
        string? cursorString,
        TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + cursorString,
            _ => table.PagingByCreatedAsync(count, cursorString),
            ttl ?? DefaultTtl,
            PagingByCreateTags);

        return result;
    }

    //

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        var result = await table.DeleteAsync(notificationId);

        await InvalidateAsync(notificationId);

        return result;
    }

    //

}