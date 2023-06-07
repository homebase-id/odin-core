using System;
using Microsoft.Data.Sqlite;
using Youverse.Core.Cryptography.Crypto;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.Sqlite.ServerDatabase
{
    public class ServerDatabase : DatabaseBase
    {
        public readonly TableCron tblCron = null;
        private CacheHelper _cache = new CacheHelper("system");

        public ServerDatabase(string connectionString, long commitFrequencyMs = 5000) : base(connectionString, commitFrequencyMs)
        {
            tblCron = new TableCron(this, _cache);
        }


        ~ServerDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception("ServerDatabase was not disposed properly.");
#else
            if (!_wasDisposed)
               Log.Error("ServerDatabase was not disposed properly.");
#endif
        }


        public override void Dispose()
        {
            Commit();

            tblCron.Dispose();;

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblCron.EnsureTableExists(dropExistingTables);
            Vacuum();
        }
    }
}