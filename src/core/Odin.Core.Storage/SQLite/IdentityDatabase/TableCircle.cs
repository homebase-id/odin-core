using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        private readonly IdentityDatabase _db;
        public TableCircle(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<CircleRecord> GetAsync(Guid circleId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, circleId);
        }
        public async Task<int> InsertAsync(CircleRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> DeleteAsync(Guid circleId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, ((IdentityDatabase)conn.db)._identityId, circleId);
        }

        // SEB:TODO make async
        public List<CircleRecord> PagingByCircleId(int count, Guid? inCursor, out Guid? nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByCircleId(conn, count, _db._identityId, inCursor, out nextCursor);
            }
        }
    }
}
