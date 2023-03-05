﻿using System;
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

        public ServerDatabase(string connectionString, ulong commitFrequencyMs = 5000) : base(connectionString, commitFrequencyMs)
        {
            tblCron = new TableCron(this);
        }


        ~ServerDatabase()
        {
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