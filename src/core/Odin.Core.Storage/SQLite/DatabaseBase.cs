using System;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;


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
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dataSource,
                Mode = SqliteOpenMode.ReadWriteCreate, // Opens the database in read/write mode and creates the database file if it does not exist
                Cache = SqliteCacheMode.Shared, // Sets the cache mode to shared, allowing multiple connections to efficiently share the same data
                Pooling = true // Enables connection pooling
            };

            // Generate the connection string
            _connectionString = builder.ToString();
            _databaseSource = dataSource;

            using (DbConnection cn = new SqliteConnection(_connectionString))
            {
                cn.Open();
                InitSqliteJournalModeWal(cn);
            }

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

        public static void AssertGuidNotEmpty(Guid? g, string message = "Guid is not allowed to be empty")
        {
            if (g == Guid.Empty)
                throw new OdinSystemException(message);
        }


        public virtual void ClearCache()
        {
        }

        public DatabaseConnection CreateDisposableConnection() // SEB:TODO make async and internal
        {
            if (_wasDisposed)
            {
                throw new ObjectDisposedException("DatabaseBase");
            }
            return new DatabaseConnection(this, _connectionString);
        }


        public virtual void Dispose()
        {
            if (_wasDisposed)
            {
                return;
            }

            _wasDisposed = true;
            GC.SuppressFinalize(this);

            // Needed on Windows to avoid file locking issues.
            // When we get here, it is assumed that all connections are closed.
            // This last bit makes sure that the connection pool is cleared and all file handles are closed.
            using DbConnection cn = new SqliteConnection(_connectionString);
            SqliteConnection.ClearPool((SqliteConnection) cn);
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public abstract Task CreateDatabaseAsync(bool dropExistingTables = true);

        public DbCommand CreateCommand()
        {
            return new SqliteCommand();
        }

        private static void InitSqliteJournalModeWal(DbConnection cn)
        {
            using var command = cn.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();
        }
    }
}
