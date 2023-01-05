using System;
using System.Data.SQLite;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class KeyValueDatabase :IDisposable
    {
        private string _connectionString;

        private SQLiteConnection _connection = null;

        private SQLiteTransaction _Transaction = null;

        public TableKeyValue tblKeyValue = null;
        public TableKeyTwoValue tblKeyTwoValue = null;
        public TableKeyThreeValue TblKeyThreeValue = null;
 		public TableInbox tblInbox = null;
        public TableOutbox tblOutbox = null;
        public TableCircle tblCircle = null;
        public TableFollowsMe tblFollow = null;
        public TableCircleMember tblCircleMember = null;

        private Object _getConnectionLock = new Object();
        private Object _getTransactionLock = new Object();


        public KeyValueDatabase(string connectionString)
        {
            _connectionString = connectionString;

            tblKeyValue = new TableKeyValue(this);
            tblKeyTwoValue = new TableKeyTwoValue(this);
            TblKeyThreeValue = new TableKeyThreeValue(this);
 			tblInbox = new TableInbox(this);
            tblOutbox = new TableOutbox(this);
            tblCircle = new TableCircle(this);
            tblCircleMember = new TableCircleMember(this);
            tblFollow = new TableFollowsMe(this);
        }


        ~KeyValueDatabase()
        {
            Dispose(false);
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
            tblFollow.EnsureTableExists(dropExistingTables);
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
                if (_Transaction == null)
                {
                    _Transaction = GetConnection().BeginTransaction();
                }
                else
                {
                    throw new Exception("Transaction already in use");
                }
            }
        }

        public void Commit()
        {
            lock (_getTransactionLock)
            {
                if (_Transaction != null)
                {
                    _Transaction.Commit();
                    _Transaction.Dispose(); // I believe these objects need to be disposed
                    _Transaction = null;
                }
            }
        }

        private void ReleaseUnmanagedResources()
        {
            // TODO release unmanaged resources here
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
            {
                _connection?.Dispose();
                _Transaction?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}