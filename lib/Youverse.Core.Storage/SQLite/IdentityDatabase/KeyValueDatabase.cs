using System;
using System.Data.SQLite;
using Youverse.Core.Cryptography.Crypto;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class KeyValueDatabase : IDisposable
    {
        private ulong _CommitFrequency; // ms
        private string _connectionString;

        private SQLiteConnection _connection = null;
        private SQLiteTransaction _transaction = null;

        public readonly TableKeyValue tblKeyValue = null;
        public readonly TableKeyTwoValue tblKeyTwoValue = null;
        public readonly TableKeyThreeValue TblKeyThreeValue = null;
        public readonly TableInbox tblInbox = null;
        public readonly TableOutbox tblOutbox = null;
        public readonly TableCircle tblCircle = null;
        public readonly TableImFollowing tblImFollowing = null;
        public readonly TableFollowsMe tblFollowsMe = null;
        public readonly TableCircleMember tblCircleMember = null;

        private Object _getConnectionLock = new Object();
        private Object _getTransactionLock = new Object();
        private UnixTimeUtc _lastCommit;
        private bool _wasDisposed = false;


        public KeyValueDatabase(string connectionString, ulong commitFrequencyMs = 5000)
        {
            _connectionString = connectionString;
            _CommitFrequency = commitFrequencyMs;

            tblKeyValue = new TableKeyValue(this, _getTransactionLock);
            tblKeyTwoValue = new TableKeyTwoValue(this, _getTransactionLock);
            TblKeyThreeValue = new TableKeyThreeValue(this, _getTransactionLock);
            tblInbox = new TableInbox(this, _getTransactionLock);
            tblOutbox = new TableOutbox(this, _getTransactionLock);
            tblCircle = new TableCircle(this, _getTransactionLock);
            tblCircleMember = new TableCircleMember(this, _getTransactionLock);
            tblFollowsMe = new TableFollowsMe(this, _getTransactionLock);
            tblImFollowing = new TableImFollowing(this, _getTransactionLock);

            RsaKeyManagement.noDBOpened++;
        }


        ~KeyValueDatabase()
        {
            RsaKeyManagement.noDBClosed++;

            // I have a freaky one in the tests that I cannot find. Argh. Below commented out.

            if (!_wasDisposed)
               throw new Exception("Was not disposed: "+ _connectionString); // Oddly, I cannot call Dispose()
            // We need to except because we may have missed a commit and we cannot call it now.
            // One of the C# corners that are broken IMO
        }


        public void Dispose()
        {
            Commit();
            _connection?.Dispose();
            _connection = null;

            _transaction?.Dispose();
            _transaction = null;

            tblKeyValue.Dispose();;
            tblKeyTwoValue.Dispose();;
            TblKeyThreeValue.Dispose();;
            tblInbox.Dispose();;
            tblOutbox.Dispose();;
            tblCircle.Dispose();;
            tblImFollowing.Dispose();;
            tblFollowsMe.Dispose();;
            tblCircleMember.Dispose();;

            _getConnectionLock = null;
            _getTransactionLock = null;
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
        public void CreateDatabase(bool dropExistingTables = true)
        {
            tblKeyValue.EnsureTableExists(dropExistingTables);
            tblKeyTwoValue.EnsureTableExists(dropExistingTables);
            TblKeyThreeValue.EnsureTableExists(dropExistingTables);
            // TblKeyUniqueThreeValue.EnsureTableExists(dropExistingTables);
            tblInbox.EnsureTableExists(dropExistingTables);
            tblOutbox.EnsureTableExists(dropExistingTables);
            tblCircle.EnsureTableExists(dropExistingTables);
            tblCircleMember.EnsureTableExists(dropExistingTables);
            tblImFollowing.EnsureTableExists(dropExistingTables);
            tblFollowsMe.EnsureTableExists(dropExistingTables);
            Vacuum();
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
                    if (UnixTimeUtc.Now().milliseconds - _lastCommit.milliseconds > _CommitFrequency)
                    {
                        Commit();
                        BeginTransaction();
                    }
                }
            }
        }

        public void Commit()
        {
            lock (_getTransactionLock)
            {
                if (_transaction != null)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }

    }
}