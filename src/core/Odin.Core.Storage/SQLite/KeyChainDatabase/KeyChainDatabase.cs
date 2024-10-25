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

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class KeyChainDatabase : DatabaseBase
    {
        public readonly TableKeyChain tblKeyChain = null;

        private readonly CacheHelper _cache = new CacheHelper("blockchain");
        private readonly string _file;
        private readonly int _line;
        public KeyChainDatabase(string dataSource, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(dataSource)
        {
            tblKeyChain = new TableKeyChain(this, _cache);

            _file = file;
            _line = line;
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblKeyChain.EnsureTableExistsAsync(conn, dropExistingTables);
            if (dropExistingTables)
            {
                await conn.VacuumAsync();
            }
        }
        
        // SEB:NOTE this is a temporary hack while we refactor the database code
        public new DatabaseConnection CreateDisposableConnection() 
        {
            return base.CreateDisposableConnection();
        }
        
    }
}