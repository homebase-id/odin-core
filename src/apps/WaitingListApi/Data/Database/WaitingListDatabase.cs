﻿using Odin.Core.Storage.SQLite;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace WaitingListApi.Data.Database
{
    public class WaitingListDatabase : DatabaseBase
    {
        public readonly WaitingListTable? WaitingListTable = null;

        public WaitingListDatabase(string connectionString) : base(connectionString)
        {
            WaitingListTable = new WaitingListTable(this);
        }


        public override void Dispose()
        {
            WaitingListTable?.Dispose();;
            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            WaitingListTable?.EnsureTableExists(dropExistingTables);
        }
    }
}