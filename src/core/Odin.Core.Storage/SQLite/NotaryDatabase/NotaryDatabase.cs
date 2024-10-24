using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.NotaryDatabase
{
    public class NotaryDatabase : DatabaseBase
    {
        public readonly TableNotaryChain tblNotaryChain = null;

        private readonly CacheHelper _cache = new CacheHelper("notarychain");
        private readonly string _file;
        private readonly int _line;
        public NotaryDatabase(string connectionString, long commitFrequencyMs = 50, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString)
        {
            tblNotaryChain = new TableNotaryChain(this, _cache);

            _file = file;
            _line = line;
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblNotaryChain.EnsureTableExistsAsync(conn, dropExistingTables);
            if (dropExistingTables)
            {
                await conn.VacuumAsync();
            }
        }
    }
}