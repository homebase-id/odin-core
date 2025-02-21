using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppNotifications(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableAppNotificationsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        return await base.GetAsync(identityKey, notificationId);
    }

    public new async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = identityKey;
        return await base.UpdateAsync(item);
    }

    public async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursor)
    {
        UnixTimeUtc? utc = null;

        if (MainIndexMeta.TryParseModifiedCursor(cursor, out var ts, out var rowId))
            utc = new UnixTimeUtc(ts);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, identityKey, utc, rowId);

        return (r, tsc == null ? null : tsc.ToString() + "," +  ri.ToString()); 
    }

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        return await base.DeleteAsync(identityKey, notificationId);
    }
}
