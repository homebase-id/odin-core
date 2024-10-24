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

        public async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(int count, Guid? inCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return await PagingByCircleIdAsync(conn, count, _db._identityId, inCursor);
            }
        }
    }
}
