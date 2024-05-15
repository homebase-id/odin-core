﻿using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Data.Sqlite;
using Odin.Core.Cryptography.Crypto;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

// Enable testing
[assembly: InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.SQLite
{

    public abstract class DatabaseBase : IDisposable
    {
        protected readonly string _connectionString;
        protected bool _wasDisposed = false;
        public readonly string _databaseSource;

        protected DatabaseBase(string dataSource)
        {
            var builder = new SqliteConnectionStringBuilder();

            // Set the properties
            builder.DataSource = dataSource;  // Replace with your database file path
            builder.Mode = SqliteOpenMode.ReadWriteCreate;  // Opens the database in read/write mode and creates the database file if it does not exist
            builder.Cache = SqliteCacheMode.Shared;  // Sets the cache mode to shared, allowing multiple connections to efficiently share the same data
            builder.Pooling = true;  // Enables connection pooling

            // Generate the connection string
            _connectionString = builder.ToString();
            _databaseSource = dataSource;

            RsaKeyManagement.noDBOpened++;
        }

        ~DatabaseBase()
        {
            RsaKeyManagement.noDBClosed++;

#if DEBUG
            if (!_wasDisposed)
                throw new Exception("Was not disposed: " + _connectionString);
#else
            if (!_wasDisposed)
                Serilog.Log.Error("Was not disposed: " + _connectionString);
#endif
        }

        public virtual void ClearCache()
        {
        }

        public DatabaseConnection CreateDisposableConnection()
        {
            return new DatabaseConnection(this, _connectionString);
        }


        public virtual void Dispose()
        {
            _wasDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public virtual void CreateDatabase(DatabaseConnection conn, bool dropExistingTables = true)
        {
            using (var pragmaJournalModeCommand = conn.Connection.CreateCommand())
            {
                pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
                pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
            }
        }

        public SqliteCommand CreateCommand()
        {
            var cmd = new SqliteCommand();

            return cmd;
        }
    }
}