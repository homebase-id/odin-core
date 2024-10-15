using System.Collections.Generic;
using System;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyTwoValue : TableKeyTwoValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyTwoValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableKeyTwoValue()
        {
        }

        public List<KeyTwoValueRecord> GetByKeyTwo(byte[] key2)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByKeyTwo(conn, _db._identityId, key2);
            }
        }

        public KeyTwoValueRecord Get(byte[] key1)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, key1);
            }
        }

        public int Delete(byte[] key1)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, key1);
            }
        }

        public int Insert(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Upsert(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }

        public int Update(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Update(conn, item);
            }
        }
    }
}
