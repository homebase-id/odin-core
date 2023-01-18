using System;

namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class TableKeyValueBase : IDisposable
    {
        protected KeyValueDatabase _keyValueDatabase = null;
        protected Object _getTransactionLock = new Object();


        public TableKeyValueBase(KeyValueDatabase db, object lck)
        {
            _keyValueDatabase = db;
            _getTransactionLock = lck;
        }

        ~TableKeyValueBase()
        {
        }

        public void Dispose()
        {
        }

        public virtual void EnsureTableExists(bool dropExisting = false)
        {
            throw new Exception("You must implement the EnsureTableExists class");
        }
    }
} 
