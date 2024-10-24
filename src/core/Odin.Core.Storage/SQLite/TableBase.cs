using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite
{
    public abstract class TableBase(string tableName)
    {
        protected readonly string TableName = tableName;
        public abstract Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false);
        public abstract List<string> GetColumnNames();
    }
} 
