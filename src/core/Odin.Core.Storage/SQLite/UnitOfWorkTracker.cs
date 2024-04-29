using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace Odin.Core.Storage.SQLite
{
    /// <summary>
    /// You MUST lock {} before using this class
    /// Locking locally leads to outer locking issues, so don't add a lock here,
    /// lock before using
    /// </summary>
    public partial class DatabaseBase
    {
        public class DatabaseConnection : IDisposable
        {
            private bool _disposed = false;
            public readonly DatabaseBase db;

            private SqliteConnection _connection;
            public SqliteTransaction _transaction = null;
            public IntCounter _counter = new IntCounter(); // The UnitOfWork counter is local to the connection
            public int _commitsCount = 0;

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

            private void BeginTransaction()
            {
                lock (_counter._lock)
                {
                    Debug.Assert(_transaction == null);
                    Debug.Assert(_connection != null);
                    _transaction = _connection.BeginTransaction();
                }
            }


            /// <summary>
            /// Don't call this function directly, use using (CreateCommitUnitOfWork()) instead
            /// </summary>
            /// <returns>True if transaction was committed</returns>
            public bool Commit()
            {
                lock (_counter._lock)
                {
                    if (_counter.ReadyToCommit())
                    {
                        if (_transaction != null)
                        {
                            _commitsCount++;
                            _transaction.Commit(); // Flush the data
                            _transaction.Dispose();
                            _transaction = null;
                            return true;
                        }
                    }

                    return false;
                }
            }

            /// <summary>
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
            public UnitOfWorkTracker CreateCommitUnitOfWork()
            {
                lock (_counter._lock)
                {
                    if (_counter.ReadyToCommit())
                        BeginTransaction();
                    return new UnitOfWorkTracker(this, _counter);
                }
            }

            public void Dispose()
            {
                if (_disposed == true)
                    return;

                lock (_counter._lock)
                {
                    _disposed = true;

                    if (_transaction != null )
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
                lock (_counter._lock)
                {
                    return _commitsCount;
                }
            }
        }


        public class IntCounter // Since I can't store a ref to an int, I make this hack and pass a pointer to the class.
        {
            private int _counter = 0;
            public Object _lock = new Object();

            public void Increment()
            {
                lock (_lock)
                {
                    _counter++;
                }
            }


            public void Decrement()
            {
                lock (_lock)
                {
                    _counter--;
                }
            }

            public int Count()
            {
                lock (_lock)
                {
                    return _counter;
                }
            }

            public bool ReadyToCommit()
            {
                lock (_lock)
                {
                    return (_counter == 0);
                }
            }
        }


        public class UnitOfWorkTracker : IDisposable
        {
            private bool _disposed = false;
            private IntCounter _counterObject;
            private readonly DatabaseConnection _connection;

            public UnitOfWorkTracker(DatabaseConnection connection, IntCounter counter)
            {
                _counterObject = counter;
                _connection = connection;
                _counterObject.Increment();
            }

            ~UnitOfWorkTracker()
            {
#if DEBUG
                throw new Exception("aiai boom, a UnitOfWorkTracker was not disposed, catastrophe, data wont get written");
#else
                Serilog.Log.Error("aiai boom, a UnitOfWorkTracker was not disposed, catastrophe, data wont get written");
#endif
            }

            public void Dispose()
            {
                if (_disposed == true)
                    return;

                _disposed = true;

                _counterObject.Decrement();

                _connection.Commit();

                GC.SuppressFinalize(this);
            }
        }
    }
}
