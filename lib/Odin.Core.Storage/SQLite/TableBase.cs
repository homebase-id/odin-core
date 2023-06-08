using System;

namespace Odin.Core.Storage.SQLite
{
    public class TableBase : IDisposable
    {
        protected readonly DatabaseBase _database = null;

        public TableBase(DatabaseBase db)
        {
            _database = db;
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
