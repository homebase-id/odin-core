using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        internal readonly IntCounter _counter = new IntCounter();

        private string _connectionString;

        private SqliteConnection _connection = null;
        private SqliteTransaction _transaction = null;

        private readonly Object _transactionLock = new Object();

        protected bool _wasDisposed = false;

        private int _commitsCount = 0;

        public DatabaseBase(string databasePath)
        {
            //Database path is the physical path on disk
            _connectionString = $"Data Source={databasePath}";

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            using (var pragmaJournalModeCommand = _connection.CreateCommand())
            {
                pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
                pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
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


        public virtual void Dispose()
        {
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
            _transaction?.Commit();
            _transaction = null;

            using (var cmd = CreateCommand())
            {
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }
        }

        public int ExecuteNonQuery(SqliteCommand command)
        {
            lock (_transactionLock) // Serialize all writes to avoid locks
            {
                command.Transaction = _transaction;
                var r = command.ExecuteNonQuery();
                command.Transaction = null;
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
            lock (_transactionLock)
            {
                if (_counter.Count() == 0)
                    BeginTransaction();
                var lcu = new LogicCommitUnit(_counter, this);
                return lcu;
            }
        }


        /// <summary>
        /// You can only have one transaction per connection. Create a new database object
        /// if you want a second transaction.
        /// </summary>
        private void BeginTransaction()
        {
            lock (_transactionLock)
            {
                if (_counter.ReadyToCommit())
                {
                    Commit();
                    Debug.Assert(_transaction == null);
                    _transaction = _connection.BeginTransaction();
                }
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
            }

            return false;
        }


        public int CommitsCount()
        {
            return _commitsCount;
        }
    }
}