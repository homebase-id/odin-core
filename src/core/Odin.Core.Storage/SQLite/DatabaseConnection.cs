using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Tasks;

[assembly:InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.SQLite
{
    public class DatabaseConnection : IDisposable
    {
        private bool _disposed = false;
        public readonly DatabaseBase db;

        private SqliteConnection _connection;
        private SqliteTransaction _transaction = null;
        private int _transactionCount = 0;
        public object _lock = new ();
        internal int _nestedCounter = 0;
        private IsolationLevel _nestedIsolationLevel;

        public SqliteConnection Connection { get { return _connection; } }

        public DatabaseConnection(DatabaseBase db, string connectionString)
        {
            this.db = db;
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
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

        private void BeginTransaction(IsolationLevel isolationLevel)
        {
            Debug.Assert(_transaction == null);
            Debug.Assert(_connection != null);
            _transaction = _connection.BeginTransaction(isolationLevel);
        }

        /// <summary>
        /// Don't call this function directly, use using (CreateCommitUnitOfWork()) instead
        /// </summary>
        /// <returns>True if transaction was committed</returns>
        private bool EndTransaction(bool commit)
        {
            try
            {
                if (_transaction == null)
                {
                    return false;
                }

                _transactionCount++;

                if (commit)
                {
                    _transaction.Commit();
                }
                else
                {
                    _transaction.Rollback();
                }

                return true;
            }
            finally
            {
                _transaction?.Dispose();
                _transaction = null;
            }
        }

        /// <summary>
        /// Not thread safe.
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
            CreateCommitUnitOfWork(IsolationLevel.Unspecified, actions);
        }

        //

        public void CreateCommitUnitOfWork(IsolationLevel isolationLevel, Action actions)
        {
            CreateCommitUnitOfWorkAsync(isolationLevel, () =>
            {
                actions();
                return Task.CompletedTask;
            }).BlockingWait();
        }

        //

        public Task CreateCommitUnitOfWorkAsync(Func<Task> actions)
        {
            return CreateCommitUnitOfWorkAsync(IsolationLevel.Unspecified, actions);
        }

        //

        public async Task CreateCommitUnitOfWorkAsync(IsolationLevel isolationLevel, Func<Task> actions)
        {
            var commit = false;

            try
            {
                lock (_lock)
                {
                    if (++_nestedCounter == 1)
                    {
                        _nestedIsolationLevel = isolationLevel;
                        BeginTransaction(isolationLevel);
                    }
                    else if (_nestedIsolationLevel != isolationLevel)
                    {
                        throw new OdinSystemException("Nested transactions must have the same isolation level");
                    }
                }

                await actions();
                commit = true;
            }
            catch (Exception e)
            {
                Serilog.Log.Error(e, "CreateCommitUnitOfWorkAsync exception: {error}", e.Message);
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    if (--_nestedCounter == 0)
                    {
                        EndTransaction(commit);
                    }
                }
            }
        }


        public int ExecuteNonQuery(SqliteCommand command)
        {
            lock (_lock) // SEB:TODO lock review
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
            lock (_lock) // SEB:TODO lock review
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
            lock (_lock)
            {
                if (_disposed)
                    return;

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

        public int TransactionCount()
        {
            return _transactionCount;
        }
    }
}
