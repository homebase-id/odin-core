using Microsoft.Data.Sqlite;
using Odin.Core.Util;
using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly:InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.SQLite
{
    public class DatabaseConnection : IDisposable
    {
        private bool _disposed = false;
        public readonly DatabaseBase db;

        private SqliteConnection _connection;
        public SqliteTransaction _transaction = null;
        public int _commitsCount = 0;
        public Object _lock = new Object();
        internal int _nestedCounter = 0;

        public SqliteConnection Connection { get { return _connection; } }

        public DatabaseConnection(DatabaseBase db, string connectionString)
        {
            this.db = db;
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            using (var pragmaJournalModeCommand = _connection.CreateCommand())
            {
                pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
                pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
            }
        }

        ~DatabaseConnection()
        {
#if DEBUG
            throw new Exception("aiai boom, a LogicalThreadConnection was not disposed, catastrophe, data wont get written");
#else
                Serilog.Log.Error("aiai boom, a LogicCommitUnit was not disposed, catastrophe, data wont get written");
#endif
        }

        public void Vacuum()
        {
            lock (_lock)
            {
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "VACUUM;";
                    cmd.Connection = Connection;
                    ExecuteNonQuery(cmd);
                }
            }
        }

        private void BeginTransaction()
        {
            Debug.Assert(_transaction == null);
            Debug.Assert(_connection != null);
            _transaction = _connection.BeginTransaction();
        }


        //
        // TODO MS.
        // private Commit & BeginTransaction
        // Commit -> CommitTransaction
        //

        /// <summary>
        /// Don't call this function directly, use using (CreateCommitUnitOfWork()) instead
        /// </summary>
        /// <returns>True if transaction was committed</returns>
        private bool CommitTransaction()
        {
            if (_transaction != null)
            {
                _commitsCount++;
                _transaction.Commit(); // Flush the data
                _transaction.Dispose();
                _transaction = null;

                return true;
            }
            return false;
        }

        /// <summary>
        /// Thread safe.
        /// This is a wrapper to logically group (same) database transactions what you want to 
        /// be sure are either committed together, or not at all. Preferably used like this
        /// using (db.CreateLogicCommitUnit())
        /// {
        ///     write one row
        ///     write another row
        /// }
        /// Having them nested means data will commit when the outer-most transaction is disposed
        /// NOTE: The memory cache updates individual rows immediately and doesn't adhere to 
        /// transactions but does get cleared in a Rollback().
        /// </summary>
        /// <returns>LogicCommitUnit disposable object</returns>

        public void CreateCommitUnitOfWork(Action actions)
        {
            lock (_lock)
            {
                try
                {
                    _nestedCounter++;
                    if (_nestedCounter == 1)
                        BeginTransaction();
                    actions();
                }
                finally
                {
                    _nestedCounter--;
                    if (_nestedCounter == 0)
                        CommitTransaction();
                }
            }
        }

        /// <summary>
        /// Not thread safe. Only 1 connection per thread.
        /// </summary>
        /// <param name="actions"></param>
        /// <returns></returns>
        public async Task CreateCommitUnitOfWorkAsync(Func<Task> actions)
        {
            try
            {
                _nestedCounter++;
                if (_nestedCounter == 1)
                    BeginTransaction(); // Assuming an asynchronous version exists

                await actions(); // Execute the actions asynchronously
            }
            finally
            {
                _nestedCounter--;
                if (_nestedCounter == 0)
                    CommitTransaction(); // Assuming an asynchronous version exists
            }
        }


        public int ExecuteNonQuery(SqliteCommand command)
        {
            lock (_lock)
            {
                command.Connection = _connection;
                command.Transaction = _transaction;
                var r = command.ExecuteNonQuery();
                command.Transaction = null;
                return r;
            }
        }


        /// <summary>
        /// You must lock connection._lock when using the reader object
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="command"></param>
        /// <param name="behavior"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public SqliteDataReader ExecuteReader(SqliteCommand command, CommandBehavior behavior)
        {
            lock (_lock)
            {
                command.Connection = _connection;
                command.Transaction = _transaction;
                var r = command.ExecuteReader();
                command.Transaction = null;
                return r;
            }
        }



        // You need to ensure BeginTransactionAsync and CommitAsync are correctly implemented to handle async operations.

        public void Dispose()
        {
            if (_disposed == true)
                return;

            lock (_lock)
            {
                _disposed = true;

                if (_transaction != null)
                {
                    Serilog.Log.Error("Connection {DatabaseSource} Disposed with an open transaction, rolling back changes.", db._databaseSource);
                    _transaction.Rollback();
                    db.ClearCache();
                }

                _transaction?.Dispose();
                _connection.Close();
                _connection?.Dispose();
                _transaction = null;
                _connection = null;

                GC.SuppressFinalize(this);
            }
        }

        public int CommitsCount()
        {
            lock (_lock)
            {
                return _commitsCount;
            }
        }
    }
}
