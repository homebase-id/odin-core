using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppNotifications(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppNotificationsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        return await base.GetAsync(odinIdentity, notificationId);
    }

    public new async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    public async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursorString)
    {
        var cursor = TimeRowCursor.FromJson(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, odinIdentity, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        return await base.DeleteAsync(odinIdentity, notificationId);
    }
}
