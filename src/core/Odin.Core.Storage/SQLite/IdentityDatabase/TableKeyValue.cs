using System;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        public TableKeyValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyValue()
        {
        }

        public KeyValueRecord Get(DatabaseConnection conn, byte[] key)
        {
            return base.Get(conn, ((IdentityDatabase)_database)._identityId, key);
        }
        public new int Insert(DatabaseConnection conn, KeyValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;

            return base.Insert(conn, item);
        }
        public int Delete(DatabaseConnection conn, byte[] key)
        {
            return base.Delete(conn, ((IdentityDatabase)_database)._identityId, key);
        }

        public new int Upsert(DatabaseConnection conn, KeyValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Upsert(conn, item);
        }

        public new int Update(DatabaseConnection conn, KeyValueRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Update(conn, item); 
        }
    }
}