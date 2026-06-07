using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public sealed record AppNotificationsPage(List<AppNotificationsRecord> Records, string Cursor);

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

    public async Task<int> UpdateUnreadAsync(List<Guid> notificationIds, bool unread)
    {
        if (notificationIds == null || notificationIds.Count == 0)
            return 0;

        var result = await table.UpdateUnreadAsync(notificationIds, unread);

        // Invalidate each affected record key plus the paging cache, in a single pass.
        var actions = notificationIds
            .Distinct()
            .Select(id => Cache.CreateRemoveByKeyAction(GetCacheKey(id)))
            .ToList();
        actions.Add(Cache.CreateRemoveByTagsAction(PagingByCreateTags));
        await Cache.InvalidateAsync(actions);

        return result;
    }

    //

    public async Task<AppNotificationsPage> PagingByCreatedAsync(
        int count,
        string? cursorString,
        TimeSpan? ttl = null)
    {
        return await Cache.GetOrSetAsync(
            "PagingByCreated" + ":" + count + ":" + cursorString,
            async _ =>
            {
                var (records, cursor) = await table.PagingByCreatedAsync(count, cursorString);
                return new AppNotificationsPage(records, cursor);
            },
            ttl ?? DefaultTtl,
            DefaultEntrySize,
            PagingByCreateTags);
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