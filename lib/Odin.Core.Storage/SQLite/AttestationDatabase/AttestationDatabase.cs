﻿using System;
using System.Runtime.CompilerServices;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationDatabase : DatabaseBase
    {
        public readonly TableAttestationRequest tblAttestationRequest = null;

        public readonly string CN;

        private readonly CacheHelper _cache = new CacheHelper("attestation");
        private readonly string _file;
        private readonly int _line;

        public AttestationDatabase(string connectionString, long commitFrequencyMs = 5000, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString, commitFrequencyMs)
        {
            tblAttestationRequest = new TableAttestationRequest(this, _cache);
            CN = connectionString;
            _file = file;
            _line = line;
        }


        ~AttestationDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"AttestationDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error($"AttestationDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void Dispose()
        {
            Commit();

            tblAttestationRequest.Dispose();

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblAttestationRequest.EnsureTableExists(dropExistingTables);
            if (dropExistingTables)
                Vacuum();
        }
    }
}
