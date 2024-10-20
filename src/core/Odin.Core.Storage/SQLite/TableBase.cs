﻿using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite
{
    public class TableBase : IDisposable
    {
        public readonly string _tableName;
        public TableBase(string tableName)
        {
            _tableName = tableName;
        }

        ~TableBase()
        {
        }

        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }

        public virtual void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
            throw new NotImplementedException();
        }

        public virtual List<string> GetColumnNames()
        {
            throw new NotImplementedException();
        }
    }
} 
