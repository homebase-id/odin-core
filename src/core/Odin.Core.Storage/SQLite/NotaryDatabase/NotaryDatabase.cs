﻿using System;
using System.Runtime.CompilerServices;

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

        public readonly string CN;

        private readonly CacheHelper _cache = new CacheHelper("notarychain");
        private readonly string _file;
        private readonly int _line;
        public NotaryDatabase(string connectionString, long commitFrequencyMs = 50, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString, commitFrequencyMs)
        {
            tblNotaryChain = new TableNotaryChain(this, _cache);
            CN = connectionString;
            _file = file;
            _line = line;
        }


        ~NotaryDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"NotaryChainDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error($"BlockChainDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void Dispose()
        {
            Commit();

            tblNotaryChain.Dispose();

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblNotaryChain.EnsureTableExists(dropExistingTables);
            if (dropExistingTables)
                Vacuum();
        }
    }
}