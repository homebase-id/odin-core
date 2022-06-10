using System;

namespace Youverse.Core.SystemStorage.SqliteKeyValue
{
    public class TableKeyValueBase
    {
        protected KeyValueDatabase _keyValueDatabase = null;


        public TableKeyValueBase(KeyValueDatabase db)
        {
            _keyValueDatabase = db;
        }

        ~TableKeyValueBase()
        {
        }


        public virtual void EnsureTableExists(bool dropExisting = false)
        {
            throw new Exception("You must implement the EnsureTableExists class");
        }
    }
} 
