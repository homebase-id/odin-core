using Odin.Core.Time;
using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase;

public class TableAppNotifications: TableAppNotificationsCRUD
{
    private readonly IdentityDatabase _db;

    public TableAppNotifications(IdentityDatabase db, CacheHelper cache) : base(cache)
    {
        _db = db;
    }

    public AppNotificationsRecord Get(Guid notificationId)
    {
        using (var myc = _db.CreateDisposableConnection())
        {
            return base.Get(myc, _db._identityId, notificationId);
        }
    }

    public int Insert(AppNotificationsRecord item)
    {
        item.identityId = _db._identityId;
        using (var myc = _db.CreateDisposableConnection())
        {
            return base.Insert(myc, item);
        }
    }

    public int Update(AppNotificationsRecord item)
    {
        item.identityId = _db._identityId;
        using (var myc = _db.CreateDisposableConnection())
        {
            return base.Update(myc, item);
        }
    }

    public List<AppNotificationsRecord> PagingByCreated(int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
    {
        using (var myc = _db.CreateDisposableConnection())
        {
            return base.PagingByCreated(myc, count, _db._identityId, inCursor, out nextCursor);
        }
    }

    public int Delete(Guid notificationId)
    {
        using (var myc = _db.CreateDisposableConnection())
        {
            return base.Delete(myc, _db._identityId, notificationId);
        }
    }
}
