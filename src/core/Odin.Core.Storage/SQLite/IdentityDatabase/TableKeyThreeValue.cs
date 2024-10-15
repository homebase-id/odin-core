using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyThreeValue : TableKeyThreeValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyThreeValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableKeyThreeValue()
        {
        }

        public KeyThreeValueRecord Get(byte[] key1)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, key1);
            }
        }

        public List<byte[]> GetByKeyTwo(byte[] key2)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByKeyTwo(conn, _db._identityId, key2);
            }
        }
        
        public List<byte[]> GetByKeyThree(byte[] key3)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByKeyThree(conn, _db._identityId, key3);
            }
        }
        public List<KeyThreeValueRecord> GetByKeyTwoThree(byte[] key2, byte[] key3)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByKeyTwoThree(conn, _db._identityId, key2, key3);
            }
        }

        public int Upsert(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }

        public int Insert(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Delete(byte[] key1)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, key1);
            }
        }

        public int Update(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Update(conn, item);
            }
        }

    }
}