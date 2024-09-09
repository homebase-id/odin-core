using System.Collections.Generic;
using System;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyTwoValue : TableKeyTwoValueCRUD
    {
        public TableKeyTwoValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyTwoValue()
        {
        }

        public List<KeyTwoValueRecord> GetByKeyTwo(DatabaseConnection conn, byte[] key2)
        {
            return base.GetByKeyTwo(conn, ((IdentityDatabase)conn.db)._identityId, key2);
        }

        public KeyTwoValueRecord Get(DatabaseConnection conn, byte[] key1)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, key1);
        }

        public int Delete(DatabaseConnection conn, byte[] key1)
        {
            return base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, key1);
        }

        public new int Insert(DatabaseConnection conn, KeyTwoValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public new int Upsert(DatabaseConnection conn, KeyTwoValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Upsert(conn, item);
        }

        public new int Update(DatabaseConnection conn, KeyTwoValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Update(conn, item); 
        }
    }
}
