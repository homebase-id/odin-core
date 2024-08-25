using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        public TableCircle(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableCircle()
        {
        }

        public CircleRecord Get(DatabaseConnection conn, Guid circleId)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, circleId);
        }
        public new int Insert(DatabaseConnection conn, CircleRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public int Delete(DatabaseConnection conn, Guid circleId)
        {
            return base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, circleId);
        }

        public List<CircleRecord> PagingByCircleId(DatabaseConnection conn, int count, Guid? inCursor, out Guid? nextCursor)
        {
            return base.PagingByCircleId(conn, count, ((IdentityDatabase)conn.db)._identityId, inCursor, out nextCursor);
        }
    }
}
