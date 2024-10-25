using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase;

public class TableAppNotifications: TableAppNotificationsCRUD
{
    private readonly IdentityDatabase _db;

    public TableAppNotifications(IdentityDatabase db, CacheHelper cache) : base(cache)
    {
        _db = db;
    }

    public async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        using var myc = _db.CreateDisposableConnection();
        return await base.GetAsync(myc, _db._identityId, notificationId);
    }

    public async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = _db._identityId;
        using var myc = _db.CreateDisposableConnection();
        return await base.InsertAsync(myc, item);
    }

    public async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = _db._identityId;
        using var myc = _db.CreateDisposableConnection();
        return await base.UpdateAsync(myc, item);
    }

    public async Task<(List<AppNotificationsRecord>, UnixTimeUtcUnique? nextCursor)> PagingByCreatedAsync(int count, UnixTimeUtcUnique? inCursor)
    {
        using var myc = _db.CreateDisposableConnection();
        return await base.PagingByCreatedAsync(myc, count, _db._identityId, inCursor);
    }

    public async Task<int> DeleteAsync(Guid notificationId)
    {
        using var myc = _db.CreateDisposableConnection();
        return await base.DeleteAsync(myc, _db._identityId, notificationId);
    }
}
