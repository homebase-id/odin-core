using Odin.Core.Time;
using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase;

public class TableAppNotifications: TableAppNotificationsCRUD
{
    public TableAppNotifications(IdentityDatabase db, CacheHelper cache) : base(db, cache)
    {
    }

    public AppNotificationsRecord Get(DatabaseConnection conn, Guid notificationId)
    {
        return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, notificationId);
    }

    public new int Insert(DatabaseConnection conn, AppNotificationsRecord item)
    {
        item.identityId = ((IdentityDatabase)conn.db)._identityId;
        return base.Insert(conn, item);
    }

    public new int Update(DatabaseConnection conn, AppNotificationsRecord item)
    {
        item.identityId = ((IdentityDatabase)conn.db)._identityId;
        return base.Update(conn, item);
    }

    public List<AppNotificationsRecord> PagingByCreated(DatabaseConnection conn, int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
    {
        return base.PagingByCreated(conn, count, ((IdentityDatabase)conn.db)._identityId, inCursor, out nextCursor); 
    }

    public int Delete(DatabaseConnection conn, Guid notificationId)
    {
        return base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, notificationId);
    }
}