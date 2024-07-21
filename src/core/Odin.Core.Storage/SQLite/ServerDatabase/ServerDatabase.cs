using System;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Odin.Core.Storage.SQLite.ServerDatabase
{
    public class ServerDatabase : DatabaseBase
    {
        // private readonly CacheHelper _cache = null; // No tables needing cache at this time.... Otherwise new CacheHelper("system");

        public ServerDatabase(string databasePath) : base(databasePath)
        {
        }


        ~ServerDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception("ServerDatabase was not disposed properly.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error("ServerDatabase was not disposed properly.");
#endif
        }


        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(DatabaseConnection conn, bool dropExistingTables = true)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            if (dropExistingTables)
                conn.Vacuum();
        }
    }
}