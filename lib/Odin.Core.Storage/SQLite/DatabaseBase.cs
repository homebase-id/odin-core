using System;
using System.Data;
using System.Timers;
using Microsoft.Data.Sqlite;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Odin.Core.Storage.SQLite
{
    public class DatabaseBase : IDisposable
    {
        public class IntCounter // Since I can't store a ref to an int, I make this hack.
        {
            public int _counter = 0;

            public bool ReadyToCommit()
            {
                return (_counter == 0);
            }
        }

        public readonly IntCounter _counter = new IntCounter();

        public class LogicCommitUnit : IDisposable
        {
            private bool _wasDisposed = false;
            private IntCounter _counterObject = null;

            public LogicCommitUnit(IntCounter counter)
            {
                _counterObject = counter;
                _counterObject._counter++;
            }

            ~LogicCommitUnit()
            {
                if (!_wasDisposed)
                    throw new Exception("aiai boom, a LogicCommitUnit was not disposed, catastrophe, data wont get written");
            }

            public void Dispose()
            {
                _counterObject._counter--;
                _wasDisposed = true;
            }

            public bool ReadyToCommit()
            {
                return (_counterObject._counter == 0);
            }
        }

        private long _commitFrequency; // ms
        private string _connectionString;

        private SqliteConnection _connection = null;
        private SqliteTransaction _transaction = null;

        private readonly Object _connectionLock = new Object();
        private readonly Object _transactionLock = new Object();

        private UnixTimeUtc _lastCommit;
        protected bool _wasDisposed = false;
        private Timer _commitTimer = new Timer();

        private int _timerTriggerCount = 0;
        private int _timerCommitTriggerCount = 0;
        private int _commitCallCount = 0;
        private int _commitFlushCount = 0;
        private bool _overdue = false;
        private bool _dataToCommit = false;

        public DatabaseBase(string connectionString, long commitFrequencyMs = 5000)
        {
            if (commitFrequencyMs < 250)
                throw new ArgumentOutOfRangeException("Minimum 250ms for now");

            _connectionString = connectionString;
            _commitFrequency = commitFrequencyMs;

            _commitTimer.Interval = _commitFrequency;
            _commitTimer.AutoReset = false;
            _commitTimer.Elapsed += OnCommitTimerEvent;

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
            _transaction = _connection.BeginTransaction();
            _lastCommit = new UnixTimeUtc();

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


        public virtual void Dispose()
        {
            _commitTimer.Dispose();

            _transaction?.Commit(); // Flush any pending data
            _transaction?.Dispose();
            _transaction = null;

            if (_connection != null)
            {
                // https://www.bricelam.net/2021/11/08/microsoft-data-sqlite-6.html
                SqliteConnection.ClearPool(_connection);
                _connection.Close();
                _connection.Dispose();
                _connection = null;
            }

            _wasDisposed = true;
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public virtual void CreateDatabase(bool dropExistingTables = true)
        {
            throw new Exception("Not implemented");
        }

        public void Vacuum()
        {
            this._transaction.Commit();
            this._transaction = null;

            using (var cmd = CreateCommand())
            {
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }

            _transaction = _connection.BeginTransaction();
            _lastCommit = new UnixTimeUtc();
        }

        public int ExecuteNonQuery(SqliteCommand command)
        {
            lock (_transactionLock)
            {
                command.Transaction = _transaction;
                var r = command.ExecuteNonQuery();
                command.Transaction = null;
                _dataToCommit = true;
                BeginTransaction();
                return r;
            }
        }

        public SqliteDataReader ExecuteReader(SqliteCommand command, CommandBehavior behavior)
        {
            lock (_transactionLock)
            {
                command.Transaction = _transaction;
                var r = command.ExecuteReader();
                command.Transaction = null;
                _dataToCommit = true;
                BeginTransaction();
                return r;
            }
        }


        public SqliteCommand CreateCommand()
        {
            return new SqliteCommand
            {
                Connection = _connection
            };
        }


        /// <summary>
        /// This is a wrapper to logically group (same) database transactions what you want to 
        /// be sure are either committed together, or not at all. Preferably used like this
        /// using (db.CreateLogicCommitUnit())
        /// {
        ///     write one row
        ///     write another row
        /// }
        /// If you want to ensure that the data is subsequently flushed to the DB (will slow it down)
        /// and you don't want to wait for the timer, then use the:
        /// db.Commit()
        /// Calling db.Commit() will be futile while one or more logic commit units are in progress.
        /// If you forget to Dispose a LogicCommitUnit you're totally screwed. Use with thought.
        /// </summary>
        /// <returns>LogicCommitUnit disposable object</returns>
        public LogicCommitUnit CreateCommitUnitOfWork()
        {
            return new LogicCommitUnit(_counter);
        }


        /// <summary>
        /// You can only have one transaction per connection. Create a new database object
        /// if you want a second transaction.
        /// </summary>
        private void BeginTransaction()
        {
            if (_overdue && _counter.ReadyToCommit())
                Commit();

            lock (_transactionLock)
            {
                // Let's check if we should commit
/*                if (UnixTimeUtc.Now().milliseconds - _lastCommit.milliseconds > _commitFrequency)
                {
                    if (Commit() == true)
                    {
                        _transaction = _connection.BeginTransaction();
                        _lastCommit = new UnixTimeUtc();
                    }
                }*/

                if (_commitTimer.Enabled == false)
                    _commitTimer.Start();
            }
        }


        /// <summary>
        /// Commit the current transaction - if possible. The transaction cannot be commited if 
        /// multiple threads are in the middle of different units of work.
        /// </summary>
        /// <returns>true is data was committed to the DB, false otherwise.</returns>
        public bool Commit()
        {
            lock (_transactionLock)
            {
                _commitCallCount++;

                if (!_counter.ReadyToCommit())
                {
                    _overdue = true;
                    return false;
                }

                _overdue = false;

                if (_dataToCommit == false)
                    return false;


                // Flush the data
                _commitTimer.Stop();
                _commitFlushCount++;
                _transaction.Commit(); // Flush the data
                _dataToCommit = false;
                _transaction.Dispose(); // I believe these objects need to be disposed
                _transaction = _connection.BeginTransaction();
                _lastCommit = new UnixTimeUtc();

                return true;
            }
        }


        public int TimerCount()
        {
            return _timerTriggerCount;
        }

        public int TimerCommitCount()
        {
            return _timerCommitTriggerCount;
        }

        public int CommitFlushCount()
        {
            return _commitFlushCount;
        }

        public int CommitCallCount()
        {
            return _commitCallCount;
        }

        private void OnCommitTimerEvent(Object source, ElapsedEventArgs e)
        {
            _timerTriggerCount++;
            if (!_counter.ReadyToCommit())
            {
                _overdue = true;
                _commitTimer.Start(); // It doesn't auto-restart, kick it back to action
                return;
            }

            _timerCommitTriggerCount++;
            Commit();
        }
    }
}