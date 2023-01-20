using System;
using System.Data.SQLite;
using Youverse.Core.Cryptography.Crypto;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class KeyValueDatabase : DatabaseBase
    {
        public readonly TableKeyValue tblKeyValue = null;
        public readonly TableKeyTwoValue tblKeyTwoValue = null;
        public readonly TableKeyThreeValue TblKeyThreeValue = null;
        public readonly TableInbox tblInbox = null;
        public readonly TableOutbox tblOutbox = null;
        public readonly TableCircle tblCircle = null;
        public readonly TableImFollowing tblImFollowing = null;
        public readonly TableFollowsMe tblFollowsMe = null;
        public readonly TableCircleMember tblCircleMember = null;

        public KeyValueDatabase(string connectionString, ulong commitFrequencyMs = 5000) : base(connectionString, commitFrequencyMs)
        {
            tblKeyValue = new TableKeyValue(this);
            tblKeyTwoValue = new TableKeyTwoValue(this);
            TblKeyThreeValue = new TableKeyThreeValue(this);
            tblInbox = new TableInbox(this);
            tblOutbox = new TableOutbox(this);
            tblCircle = new TableCircle(this);
            tblCircleMember = new TableCircleMember(this);
            tblFollowsMe = new TableFollowsMe(this);
            tblImFollowing = new TableImFollowing(this);
        }


        ~KeyValueDatabase()
        {
        }


        public override void Dispose()
        {
            Commit();

            tblKeyValue.Dispose();;
            tblKeyTwoValue.Dispose();;
            TblKeyThreeValue.Dispose();;
            tblInbox.Dispose();;
            tblOutbox.Dispose();;
            tblCircle.Dispose();;
            tblImFollowing.Dispose();;
            tblFollowsMe.Dispose();;
            tblCircleMember.Dispose();;

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblKeyValue.EnsureTableExists(dropExistingTables);
            tblKeyTwoValue.EnsureTableExists(dropExistingTables);
            TblKeyThreeValue.EnsureTableExists(dropExistingTables);
            // TblKeyUniqueThreeValue.EnsureTableExists(dropExistingTables);
            tblInbox.EnsureTableExists(dropExistingTables);
            tblOutbox.EnsureTableExists(dropExistingTables);
            tblCircle.EnsureTableExists(dropExistingTables);
            tblCircleMember.EnsureTableExists(dropExistingTables);
            tblImFollowing.EnsureTableExists(dropExistingTables);
            tblFollowsMe.EnsureTableExists(dropExistingTables);

            Vacuum();
        }
    }
}