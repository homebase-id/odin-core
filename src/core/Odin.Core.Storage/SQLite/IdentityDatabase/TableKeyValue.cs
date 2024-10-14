using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableKeyValue()
        {
        }

        public new int GetCountDirty(DatabaseConnection conn)
        {
            return base.GetCountDirty(conn);
        }

        public KeyValueRecord Get(byte[] key)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, key);
            }
        }
        public int Insert(KeyValueRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }
        public int Delete(byte[] key)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, key);
            }
        }

        public int Upsert(KeyValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }

        public int UpsertMany(List<KeyValueRecord> items)
        {
            int affectedRows = 0;

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
                {
                    foreach (var item in items)
                    {
                        item.identityId = _db._identityId;
                        affectedRows += base.Upsert(conn, item);
                    }
                });
            }
            return affectedRows;
        }

        public int Update(KeyValueRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Update(conn, item);
            }
        }
    }
}