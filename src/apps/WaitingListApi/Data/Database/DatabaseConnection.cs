using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nito.AsyncEx;

namespace WaitingListApi.Data.Database
{
    public class DatabaseConnection : IDisposable
    {
        private bool _disposed = false;
        public readonly DatabaseBase db;

        private DbConnection _connection;
        private DbTransaction _transaction = null;
        private int _transactionCount = 0;
        private readonly AsyncLock _lock = new ();
        internal int _nestedCounter = 0;

        internal DbConnection Connection { get { return _connection; } }

        public DatabaseConnection(DatabaseBase db, string connectionString)
        {
            this.db = db;
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
        }

        ~DatabaseConnection()
        {
#if DEBUG
            throw new Exception("aiai boom, a DatabaseConnection was not disposed");
#else
            Serilog.Log.Error("aiai boom, a DatabaseConnection was not disposed");
#endif
        }

        internal async Task VacuumAsync()
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "VACUUM;";
            cmd.Connection = (SqliteConnection) Connection;
            await ExecuteNonQueryAsync(cmd);
        }

        private async Task BeginTransactionAsync()
        {
            ArgumentNullException.ThrowIfNull(_connection);
            if (_transaction != null)
                throw new ArgumentException("transaction already in use on this connection.");
            _transaction = await _connection.BeginTransactionAsync();
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
        private async Task<bool> EndTransactionAsync(bool commit)
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
                    await _transaction.CommitAsync();
                }
                else
                {
                    await _transaction.RollbackAsync();
                    db.ClearCache();
                }

                return true;
            }
            finally
            {
                await _transaction!.DisposeAsync();
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

        //

        public async Task CreateCommitUnitOfWorkAsync(
            Func<Task> actions,
            [CallerMemberName] string caller = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            var commit = false;

            try
            {
                using (await _lock.LockAsync())
                {
                    if (++_nestedCounter == 1)
                    {
                        if (Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
                        {
                            Serilog.Log.Verbose(
                                "CreateCommitUnitOfWorkAsync: {caller} BeginTransaction ({filePath}:{lineNumber})",
                                caller, Path.GetFileName(filePath), lineNumber);
                        }
                        await BeginTransactionAsync();
                    }
                }

                await actions();
                commit = true;
            }
            finally
            {
                using (await _lock.LockAsync())
                {
                    if (--_nestedCounter == 0)
                    {
                        await EndTransactionAsync(commit);
                        if (Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
                        {
                            Serilog.Log.Verbose(
                                "CreateCommitUnitOfWorkAsync: {caller} EndTransaction({commit}) ({filePath}:{lineNumber})",
                                caller, commit, Path.GetFileName(filePath), lineNumber);
                        }
                    }
                    else if (_nestedCounter < 0)
                    {
                        // Sanity - this should never happen
                        Serilog.Log.Error("CreateCommitUnitOfWorkAsync: {_nestedCounter}", _nestedCounter);
                    }
                }
            }
        }


        public async Task<int> ExecuteNonQueryAsync(DbCommand command)
        {
            using (await _lock.LockAsync())
            {
                command.Connection = _connection;
                command.Transaction = _transaction;
                var r = await command.ExecuteNonQueryAsync();
                command.Transaction = null;
                return r;
            }
        }

        public async Task<object>ExecuteScalarAsync(DbCommand command)
        {
            using (await _lock.LockAsync())
            {
                command.Connection = _connection;
                command.Transaction = _transaction;
                var r = await command.ExecuteScalarAsync();
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
        public async Task<DbDataReader> ExecuteReaderAsync(DbCommand command, CommandBehavior behavior)
        {
            using (await _lock.LockAsync())
            {
                command.Connection = _connection;
                command.Transaction = _transaction;
                var r = await command.ExecuteReaderAsync();
                command.Transaction = null;
                return r;
            }
        }



        // You need to ensure BeginTransactionAsync and CommitAsync are correctly implemented to handle async operations.

        public void Dispose()
        {
            using (_lock.Lock())
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

        internal int TransactionCount()
        {
            return _transactionCount;
        }
    }
}
