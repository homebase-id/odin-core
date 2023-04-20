


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
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

        public readonly string CN;
        public IdentityDatabase(string connectionString, long commitFrequencyMs = 5000) : base(connectionString, commitFrequencyMs)
        {
            tblAppGrants = new TableAppGrants(this);
            tblKeyValue = new TableKeyValue(this);
            tblKeyTwoValue = new TableKeyTwoValue(this);
            TblKeyThreeValue = new TableKeyThreeValue(this);
            tblInbox = new TableInbox(this);
            tblOutbox = new TableOutbox(this);
            tblFeedDistributionOutbox = new TableFeedDistributionOutbox(this);
            tblCircle = new TableCircle(this);
            tblCircleMember = new TableCircleMember(this);
            tblFollowsMe = new TableFollowsMe(this);
            tblImFollowing = new TableImFollowing(this);
            tblConnections = new TableConnections(this);

            CN = connectionString;
        }


        ~IdentityDatabase()
        {
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

            Vacuum();
        }
    }
}