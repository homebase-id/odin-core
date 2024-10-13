using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableConnections : TableConnectionsCRUD
    {
        private readonly IdentityDatabase _db;

        public TableConnections(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableConnections()
        {
        }

        public ConnectionsRecord Get(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, identity);
            }
        }

        public int Insert(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Upsert(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }

        public int Update(ConnectionsRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Update(conn, item);
            }
        }

        public int Delete(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, identity);
            }
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
