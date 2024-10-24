using Odin.Core.Exceptions;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class IdentityDatabase : DatabaseBase
    {
        public class NullableGuid
        {
            public Guid? uniqueId;
        }

        //
        public readonly MainIndexMeta metaIndex = null;

        // Drive tables
        public readonly TableDriveMainIndex tblDriveMainIndex = null;
        public readonly TableDriveAclIndex tblDriveAclIndex = null;
        public readonly TableDriveTagIndex tblDriveTagIndex = null;
        public readonly TableDriveReactions tblDriveReactions = null;

        // Identity tables
        public readonly TableAppGrants tblAppGrants = null;
        public readonly TableKeyValue tblKeyValue = null;
        public readonly TableKeyTwoValue tblKeyTwoValue = null;
        public readonly TableKeyThreeValue TblKeyThreeValue = null;
        public readonly TableInbox tblInbox = null;
        public readonly TableOutbox tblOutbox = null;
        public readonly TableImFollowing tblImFollowing = null;
        public readonly TableFollowsMe tblFollowsMe = null;
        public readonly TableCircle tblCircle = null;
        public readonly TableCircleMember tblCircleMember = null;
        public readonly TableConnections tblConnections = null;
        public readonly TableAppNotifications tblAppNotificationsTable = null;

        // Other
        public readonly Guid _identityId;
        public readonly CacheHelper _cache = new CacheHelper("identity");
        private readonly string _file;
        private readonly int _line;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="identityId">The unique GUID representing an Identity</param>
        /// <param name="databasePath">The path to the database file</param>
        /// <param name="file">Leave default</param>
        /// <param name="line">Leave default</param>
        public IdentityDatabase(Guid identityId, string databasePath, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(databasePath)
        {
            if (identityId == Guid.Empty)
                throw new ArgumentException("identityId cannot be Empty Guid");

            metaIndex = new MainIndexMeta(this);

            // Drive
            tblDriveMainIndex = new TableDriveMainIndex(this, _cache);
            tblDriveAclIndex = new TableDriveAclIndex(this, _cache);
            tblDriveTagIndex = new TableDriveTagIndex(this, _cache);
            tblDriveReactions = new TableDriveReactions(this, _cache);

            // Identity
            tblAppGrants = new TableAppGrants(this, _cache);
            tblKeyValue = new TableKeyValue(this, _cache);
            tblKeyTwoValue = new TableKeyTwoValue(this, _cache);
            TblKeyThreeValue = new TableKeyThreeValue(this, _cache);
            tblInbox = new TableInbox(this, _cache);
            tblOutbox = new TableOutbox(this, _cache);
            tblCircle = new TableCircle(this, _cache);
            tblCircleMember = new TableCircleMember(this, _cache);
            tblFollowsMe = new TableFollowsMe(this, _cache);
            tblImFollowing = new TableImFollowing(this, _cache);
            tblConnections = new TableConnections(this, _cache);
            tblAppNotificationsTable = new TableAppNotifications(this, _cache);

            _file = file;
            _line = line;
            _identityId = identityId;
        }

        public override void ClearCache()
        {
            _cache.ClearCache();
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = this.CreateDisposableConnection();
            
            // Drives
            await tblDriveMainIndex.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblDriveAclIndex.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblDriveTagIndex.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblDriveReactions.EnsureTableExistsAsync(conn, dropExistingTables);

            // Identity
            await tblAppGrants.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblKeyValue.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblKeyTwoValue.EnsureTableExistsAsync(conn, dropExistingTables);
            await TblKeyThreeValue.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblInbox.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblOutbox.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblCircle.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblCircleMember.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblImFollowing.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblFollowsMe.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblConnections.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblAppNotificationsTable.EnsureTableExistsAsync(conn, dropExistingTables);

            if (dropExistingTables)
            {
                await conn.VacuumAsync(); 
            }
        }


        public string FileIdToPath(Guid fileid)
        {
            // Ensure even distribution
            byte[] ba = fileid.ToByteArray();
            var b1 = ba[ba.Length - 1] & (byte)0x0F;
            var b2 = (ba[ba.Length - 1] & (byte)0xF0) >> 4;

            return b2.ToString("X1") + "/" + b1.ToString("X1");
        }
    }
}