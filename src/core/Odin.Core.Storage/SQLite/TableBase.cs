using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite
{
    public abstract class TableBase : IDisposable
    {
        public readonly string _tableName;
        public TableBase(string tableName)
        {
            _tableName = tableName;
        }

        // SEB:TODO delete
        ~TableBase()
        {
        }

        // SEB:TODO delete
        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }

        public abstract Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false);

        public abstract List<string> GetColumnNames();
    }
} 
