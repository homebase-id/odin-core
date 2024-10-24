using System;
using System.Threading.Tasks;


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
        public readonly TableJobs tblJobs = null;
        private readonly CacheHelper _cache = null; // No tables needing cache at this time.... Otherwise new CacheHelper("system");

        public ServerDatabase(string databasePath) : base(databasePath)
        {
            tblJobs = new TableJobs(this, _cache);
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblJobs.EnsureTableExistsAsync(conn, dropExistingTables);
            if (dropExistingTables)
            {
                await conn.VacuumAsync();
            }
        }
    }
}