using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableAppNotificationsCached(
    TableAppNotifications table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
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

    public async Task<AppNotificationsRecord?> GetAsync(Guid notificationId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetCacheKey(notificationId),
            _ => table.GetAsync(notificationId),
            ttl);
        return result;
    }

    //

    public async Task<int> InsertAsync(AppNotificationsRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);

        await SetAsync(
            GetCacheKey(item),
            item,
            ttl);

        await RemoveByTagAsync(PagingByCreateTags);

        return result;

    }

    //

    public async Task<int> UpdateAsync(AppNotificationsRecord item, TimeSpan ttl)
    {
        var result = await table.UpdateAsync(item);

        await SetAsync(
            GetCacheKey(item),
            item,
            ttl);

        await RemoveByTagAsync(PagingByCreateTags);

        return result;
    }

    //

    public async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(
        int count,
        string? cursorString,
        TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + cursorString,
            _ => table.PagingByCreatedAsync(count, cursorString),
            ttl,
            PagingByCreateTags);

        return result;
    }

    //

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        var result = await table.DeleteAsync(notificationId);
        await RemoveAsync(GetCacheKey(notificationId));
        await RemoveByTagAsync(PagingByCreateTags);
        return result;
    }

    //

}