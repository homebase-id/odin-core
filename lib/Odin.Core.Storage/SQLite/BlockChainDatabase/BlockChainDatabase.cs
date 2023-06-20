using System;
using System.Runtime.CompilerServices;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.BlockChainDatabase
{
    public class BlockChainDatabase : DatabaseBase
    {
        public readonly TableBlockChain tblBlockChain = null;

        public readonly string CN;

        private readonly CacheHelper _cache = new CacheHelper("blockchain");
        private readonly string _file;
        private readonly int _line;
        public BlockChainDatabase(string connectionString, long commitFrequencyMs = 5000, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString, commitFrequencyMs)
        {
            tblBlockChain = new TableBlockChain(this, _cache);
            CN = connectionString;
            _file = file;
            _line = line;
        }


        ~BlockChainDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"BlockChainDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Log.Error($"BlockChainDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void Dispose()
        {
            Commit();

            tblBlockChain.Dispose();

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblBlockChain.EnsureTableExists(dropExistingTables);
            Vacuum();
        }
    }
}