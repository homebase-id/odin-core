using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableConnections : TableConnectionsCRUD
    {
        private readonly IdentityDatabase _db;

        public TableConnections(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<ConnectionsRecord> GetAsync(OdinId identity)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, identity);
        }

        public async Task<int> InsertAsync(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> UpsertAsync(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }

        public async Task<int> UpdateAsync(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpdateAsync(conn, item);
        }

        public async Task<int> DeleteAsync(OdinId identity)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, identity);
        }

        public List<ConnectionsRecord> PagingByIdentity(int count, string inCursor, out string nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByIdentity(conn, count, _db._identityId, inCursor, out nextCursor);
            }
        }

        public List<ConnectionsRecord> PagingByIdentity(int count, Int32 status, string inCursor, out string nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByIdentity(conn, count, _db._identityId, status, inCursor, out nextCursor);
            }
        }


        public List<ConnectionsRecord> PagingByCreated(int count, Int32 status, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByCreated(conn, count, _db._identityId, status, inCursor, out nextCursor);
            }
        }

        public List<ConnectionsRecord> PagingByCreated(int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.PagingByCreated(conn, count, _db._identityId, inCursor, out nextCursor);
            }
        }
    }
}
