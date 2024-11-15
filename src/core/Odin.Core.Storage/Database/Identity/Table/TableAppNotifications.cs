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
    public Guid IdentityId { get; } = identityKey.Id;

    public async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        return await base.GetAsync(IdentityId, notificationId);
    }

    public override async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public override async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpdateAsync(item);
    }

    public async Task<(List<AppNotificationsRecord>, UnixTimeUtcUnique? nextCursor)> PagingByCreatedAsync(int count, UnixTimeUtcUnique? inCursor)
    {
        return await base.PagingByCreatedAsync(count, IdentityId, inCursor);
    }

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        return await base.DeleteAsync(IdentityId, notificationId);
    }
}
