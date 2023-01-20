using System;
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
        public readonly Object _getTransactionLock = new Object();

        private ulong _commitFrequency; // ms
        private string _connectionString;

        private SQLiteConnection _connection = null;
        private SQLiteTransaction _transaction = null;

        private Object _getConnectionLock = new Object();

        private UnixTimeUtc _lastCommit;
        private bool _wasDisposed = false;
        private Timer _commitTimer = new Timer();

        private int _timerCount = 0;
        private int _timerCommitCount = 0;


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
            Commit();

            _commitTimer.Dispose();

            _connection?.Dispose();
            _connection = null;

            _transaction?.Dispose();
            _transaction = null;

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
                _commitTimer.Stop();
                if (_transaction != null)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }

        public int TimerCount()
        {
            return _timerCount;
        }

        public int TimerCommitCount()
        {
            return _timerCommitCount;
        }

        private void OnCommitTimerEvent(Object source, ElapsedEventArgs e)
        {
            _timerCount++;
            _timerCommitCount++;
            Commit();
        }
    }
}