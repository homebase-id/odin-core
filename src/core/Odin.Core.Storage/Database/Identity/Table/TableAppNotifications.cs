using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppNotifications(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppNotificationsCRUD(scopedConnectionFactory)
{
    internal async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        return await base.GetAsync(odinIdentity, notificationId);
    }

    internal new async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    internal async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursorString)
    {
        var cursor = TimeRowCursor.FromJson(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, odinIdentity, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }

    internal async Task<int> DeleteAsync(Guid notificationId)
    {
        return await base.DeleteAsync(odinIdentity, notificationId);
    }
}
