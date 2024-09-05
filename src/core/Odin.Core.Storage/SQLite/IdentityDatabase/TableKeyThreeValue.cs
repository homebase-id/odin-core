using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyThreeValue : TableKeyThreeValueCRUD
    {
        public TableKeyThreeValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyThreeValue()
        {
        }

        public KeyThreeValueRecord Get(DatabaseConnection conn, byte[] key1)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, key1);
        }

        public List<byte[]> GetByKeyTwo(DatabaseConnection conn, byte[] key2)
        {
            return base.GetByKeyTwo(conn, ((IdentityDatabase)conn.db)._identityId, key2);
        }
        
        public List<byte[]> GetByKeyThree(DatabaseConnection conn, byte[] key3)
        {
            return base.GetByKeyThree(conn, ((IdentityDatabase)conn.db)._identityId, key3);
        }
        public List<KeyThreeValueRecord> GetByKeyTwoThree(DatabaseConnection conn, byte[] key2, byte[] key3)
        {
            return base.GetByKeyTwoThree(conn, ((IdentityDatabase)conn.db)._identityId, key2, key3);
        }

        public new int Upsert(DatabaseConnection conn, KeyThreeValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Upsert(conn, item);
        }

        public new int Insert(DatabaseConnection conn, KeyThreeValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public int Delete(DatabaseConnection conn, byte[] key1)
        {
            return base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, key1);
        }

        public new int Update(DatabaseConnection conn, KeyThreeValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Update(conn, item);
        }

    }
}