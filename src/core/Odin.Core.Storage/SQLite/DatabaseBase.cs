using System;
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

    public class DatabaseBase : IDisposable
    {
        protected readonly string _connectionString;
        protected bool _wasDisposed = false;
        public readonly string _databaseSource;

        public DatabaseBase(string dataSource)
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
            throw new Exception("Not implemented");
        }

        public SqliteCommand CreateCommand()
        {
            var cmd = new SqliteCommand();

            return cmd;
        }

        // TODO: this should really be part of CreateDatabase, but CreateDatabase is called in strange places,
        // o we leave it here for now
        private bool _journalModeInitialized;
        private readonly object _journalModeLock = new();
        public void InitSqliteJournalModeWal(SqliteConnection cn)
        {
            if (_journalModeInitialized)
            {
                return;
            }

            lock (_journalModeLock)
            {
                if (_journalModeInitialized)
                {
                    return;
                }

                _journalModeInitialized = true;

                using var command = cn.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA synchronous=NORMAL;";
                command.ExecuteNonQuery();
            }
        }
    }
}