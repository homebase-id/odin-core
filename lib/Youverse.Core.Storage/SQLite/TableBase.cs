using System;

namespace Youverse.Core.Storage.SQLite
{
    public class TableBase : IDisposable
    {
        protected readonly DatabaseBase _database = null;
        protected readonly Object _getTransactionLock = null;

        public TableBase(DatabaseBase db, object lck)
        {
            _database = db;
            _getTransactionLock = lck;
        }

        ~TableBase()
        {
        }

        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }

        public virtual void EnsureTableExists(bool dropExisting = false)
        {
            throw new NotImplementedException();
        }
    }
} 
