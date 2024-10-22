using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        private readonly IdentityDatabase _db;
        public TableCircle(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        ~TableCircle()
        {
        }

        public CircleRecord Get(Guid circleId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, circleId);
            }
        }
        public int Insert(CircleRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Delete(Guid circleId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, circleId);
            }
        }

        public List<CircleRecord> PagingByCircleId(int count, Guid? inCursor, out Guid? nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByCircleId(conn, count, _db._identityId, inCursor, out nextCursor);
            }
        }
    }
}
