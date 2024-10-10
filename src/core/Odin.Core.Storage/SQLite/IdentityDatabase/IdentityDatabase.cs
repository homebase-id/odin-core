using Odin.Core.Exceptions;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;


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


        ~IdentityDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"IdentityDatabase was not disposed properly [CN={_connectionString}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error($"IdentityDatabase was not disposed properly [CN={_connectionString}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void ClearCache()
        {
            _cache.ClearCache();
        }


        public override void Dispose()
        {
            Serilog.Log.Information("IdentityDatabase Dispose() called {_databaseSource}.", _databaseSource);

            // Drives
            tblDriveMainIndex.Dispose();
            tblDriveAclIndex.Dispose();
            tblDriveTagIndex.Dispose();
            tblDriveReactions.Dispose();

            // Identity
            tblAppGrants.Dispose();
            tblKeyValue.Dispose();
            tblKeyTwoValue.Dispose();
            TblKeyThreeValue.Dispose();
            tblInbox.Dispose();
            tblOutbox.Dispose();
            tblCircle.Dispose();
            tblImFollowing.Dispose();
            tblFollowsMe.Dispose();
            tblCircleMember.Dispose();
            tblConnections.Dispose();
            tblAppNotificationsTable.Dispose();

            base.Dispose();
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            using (var conn = this.CreateDisposableConnection())
            {
                // Drives
                tblDriveMainIndex.EnsureTableExists(conn, dropExistingTables);
                tblDriveAclIndex.EnsureTableExists(conn, dropExistingTables);
                tblDriveTagIndex.EnsureTableExists(conn, dropExistingTables);
                tblDriveReactions.EnsureTableExists(conn, dropExistingTables);

                // Identity
                tblAppGrants.EnsureTableExists(conn, dropExistingTables);
                tblKeyValue.EnsureTableExists(conn, dropExistingTables);
                tblKeyTwoValue.EnsureTableExists(conn, dropExistingTables);
                TblKeyThreeValue.EnsureTableExists(conn, dropExistingTables);
                // TblKeyUniqueThreeValue.EnsureTableExists(conn, dropExistingTables);
                tblInbox.EnsureTableExists(conn, dropExistingTables);
                tblOutbox.EnsureTableExists(conn, dropExistingTables);
                tblCircle.EnsureTableExists(conn, dropExistingTables);
                tblCircleMember.EnsureTableExists(conn, dropExistingTables);
                tblImFollowing.EnsureTableExists(conn, dropExistingTables);
                tblFollowsMe.EnsureTableExists(conn, dropExistingTables);
                tblConnections.EnsureTableExists(conn, dropExistingTables);
                tblAppNotificationsTable.EnsureTableExists(conn, dropExistingTables);

                if (dropExistingTables)
                    conn.Vacuum();
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