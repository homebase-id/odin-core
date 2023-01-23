﻿using System;
using System.Data.SQLite;
using Youverse.Core.Cryptography.Crypto;
using System.Timers;
using Serilog;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.SQLite
{
    public class DatabaseBase : IDisposable
    {
        private readonly Object _getTransactionLock = new Object();

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

        private ulong _commitFrequency; // ms
        private string _connectionString;

        private SQLiteConnection _connection = null;
        private SQLiteTransaction _transaction = null;

        private Object _getConnectionLock = new Object();

        private UnixTimeUtc _lastCommit;
        private bool _wasDisposed = false;
        private Timer _commitTimer = new Timer();

        private int _timerTriggerCount = 0;
        private int _timerCommitTriggerCount = 0;
        private int _commitCallCount = 0;
        private int _commitFlushCount = 0;


        public DatabaseBase(string connectionString, ulong commitFrequencyMs = 5000)
        {
            if (commitFrequencyMs < 250)
                throw new ArgumentOutOfRangeException("Minimum 250ms for now");

            _connectionString = connectionString;
            _commitFrequency = commitFrequencyMs;

            _commitTimer.Interval = _commitFrequency;
            _commitTimer.AutoReset = false;
            _commitTimer.Elapsed += OnCommitTimerEvent;

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
               Log.Error("Was not disposed: " + _connectionString);
#endif
        }


        public virtual void Dispose()
        {
            _commitTimer.Dispose();

            _transaction?.Commit(); // Flush any pending data
            _transaction?.Dispose();
            _transaction = null;

            _connection?.Dispose();
            _connection = null;

            _getConnectionLock = null;

            _wasDisposed = true;
        }

        public SQLiteCommand CreateCommand()
        {
            return new SQLiteCommand(GetConnection());
        }

        public void Vacuum()
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Create and return the database connection if it's not already created.
        /// Otherwise simply return the already created connection (one object needed per
        /// thread). 
        /// There's ONE connection per database object.
        /// </summary>
        /// <returns></returns>
        public SQLiteConnection GetConnection()
        {
            lock (_getConnectionLock)
            {
                if (_connection == null)
                {
                    _connection = new SQLiteConnection(_connectionString);
                    _connection.Open();
                }

                return _connection;
            }
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public virtual void CreateDatabase(bool dropExistingTables = true)
        {
            throw new Exception("Not implemented");
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
        public void BeginTransaction()
        {
            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    _lastCommit = new UnixTimeUtc();
                }
                else
                {
                    // We already had a transaction, let's check if we should commit
                    if (UnixTimeUtc.Now().milliseconds - _lastCommit.milliseconds > _commitFrequency)
                    {
                        Commit();
                        BeginTransaction();
                    }
                }

                _commitTimer.Start();
            }
        }


        public void Commit()
        {
            lock (_getTransactionLock)
            {
                _commitCallCount++;

                if (!_counter.ReadyToCommit())
                    return;

                _commitTimer.Stop();
                if (_transaction != null)
                {
                    _commitFlushCount++;
                    _transaction.Commit(); // Flush the data
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
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
                _commitTimer.Start(); // It doesn't auto-restart, kick it back to action
                return;
            }

            _timerCommitTriggerCount++;
            Commit();
        }
    }
}