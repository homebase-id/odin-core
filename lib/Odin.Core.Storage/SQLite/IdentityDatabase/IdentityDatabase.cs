


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


using System;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class IdentityDatabase : DatabaseBase
    {
        public readonly TableAppGrants tblAppGrants = null;
        public readonly TableKeyValue tblKeyValue = null;
        public readonly TableKeyTwoValue tblKeyTwoValue = null;
        public readonly TableKeyThreeValue TblKeyThreeValue = null;
        public readonly TableInbox tblInbox = null;
        public readonly TableOutbox tblOutbox = null;
        public readonly TableFeedDistributionOutbox tblFeedDistributionOutbox = null;
        public readonly TableImFollowing tblImFollowing = null;
        public readonly TableFollowsMe tblFollowsMe = null;
        public readonly TableCircle tblCircle = null;
        public readonly TableCircleMember tblCircleMember = null;
        public readonly TableConnections tblConnections = null;
        public readonly TableAppNotifications appNotificationsTable = null;

        public readonly string CN;

        public readonly CacheHelper _cache = new CacheHelper("identity"); // This is really private, but I need to access it in my test.
        private readonly string _file;
        private readonly int _line;

        public IdentityDatabase(string connectionString, long commitFrequencyMs = 5000, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString, commitFrequencyMs)
        {
            tblAppGrants = new TableAppGrants(this, _cache);
            tblKeyValue = new TableKeyValue(this, _cache);
            tblKeyTwoValue = new TableKeyTwoValue(this, _cache);
            TblKeyThreeValue = new TableKeyThreeValue(this, _cache);
            tblInbox = new TableInbox(this, _cache);
            tblOutbox = new TableOutbox(this, _cache);
            tblFeedDistributionOutbox = new TableFeedDistributionOutbox(this, _cache);
            tblCircle = new TableCircle(this, _cache);
            tblCircleMember = new TableCircleMember(this, _cache);
            tblFollowsMe = new TableFollowsMe(this, _cache);
            tblImFollowing = new TableImFollowing(this, _cache);
            tblConnections = new TableConnections(this, _cache);
            appNotificationsTable = new TableAppNotifications(this, _cache);

            CN = connectionString;

            _file = file;
            _line = line;
        }


        ~IdentityDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"IdentityDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error($"IdentityDatabase was not disposed properly [CN={CN}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void Dispose()
        {
            Commit();

            tblAppGrants.Dispose();
            tblKeyValue.Dispose();
            tblKeyTwoValue.Dispose();
            TblKeyThreeValue.Dispose();
            tblInbox.Dispose();
            tblOutbox.Dispose();
            tblFeedDistributionOutbox.Dispose();
            tblCircle.Dispose();
            tblImFollowing.Dispose();
            tblFollowsMe.Dispose();
            tblCircleMember.Dispose();
            tblConnections.Dispose();

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            tblAppGrants.EnsureTableExists(dropExistingTables);
            tblKeyValue.EnsureTableExists(dropExistingTables);
            tblKeyTwoValue.EnsureTableExists(dropExistingTables);
            TblKeyThreeValue.EnsureTableExists(dropExistingTables);
            // TblKeyUniqueThreeValue.EnsureTableExists(dropExistingTables);
            tblInbox.EnsureTableExists(dropExistingTables);
            tblOutbox.EnsureTableExists(dropExistingTables);
            tblFeedDistributionOutbox.EnsureTableExists(dropExistingTables);
            tblCircle.EnsureTableExists(dropExistingTables);
            tblCircleMember.EnsureTableExists(dropExistingTables);
            tblImFollowing.EnsureTableExists(dropExistingTables);
            tblFollowsMe.EnsureTableExists(dropExistingTables);
            tblConnections.EnsureTableExists(dropExistingTables);

            if (dropExistingTables)
                Vacuum();
        }
    }
}