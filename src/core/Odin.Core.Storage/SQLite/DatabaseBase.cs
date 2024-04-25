using System;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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

    public partial class DatabaseBase : IDisposable
    {
        protected readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(20); // Max 20 concurrent connections
 
        protected bool _wasDisposed = false;
        protected readonly string _databaseSource;

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


        public DatabaseConnection CreateDisposableConnection()
        {
            _semaphore.WaitAsync(); // Wait to enter the semaphore

            return new DatabaseConnection(this, _connectionString);
        }


        public virtual void Dispose()
        {
            if (!_wasDisposed)
            {
                using (var connection = CreateDisposableConnection())
                    SqliteConnection.ClearPool(connection._connection);
            }
            _wasDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public virtual void CreateDatabase(DatabaseBase.DatabaseConnection conn, bool dropExistingTables = true)
        {
            throw new Exception("Not implemented");
        }

        public void Vacuum()
        {
            using (var conn = this.CreateDisposableConnection()) 
            {
                using (var cmd = CreateCommand(conn))
                {
                    cmd.CommandText = "VACUUM;";
                    ExecuteNonQuery(conn, cmd);
                }
            }
        }

        public int ExecuteNonQuery(DatabaseConnection connection, SqliteCommand command)
        {
            if (connection.db != this)
                throw new ArgumentException("connection and database object mismatch");

            command.Connection = connection._connection;
            command.Transaction = connection._transaction;
            var r = command.ExecuteNonQuery();
            command.Transaction = null;
            return r;
        }

        public SqliteDataReader ExecuteReader(DatabaseConnection connection, SqliteCommand command, CommandBehavior behavior)
        {
            if (connection.db != this)
                throw new ArgumentException("connection and database object mismatch");

            command.Connection = connection._connection;
            command.Transaction = connection._transaction;
            var r = command.ExecuteReader();
            command.Transaction = null;
            return r;
        }


        public SqliteCommand CreateCommand(DatabaseConnection connection)
        {
            if (connection.db != this)
                throw new ArgumentException("connection and database object mismatch");

            var cmd = new SqliteCommand();
            cmd.Connection = connection._connection;

            return cmd;
        }
    }
}