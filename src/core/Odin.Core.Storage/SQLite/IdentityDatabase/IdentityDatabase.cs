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
        private object _dbLock = new object();

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




        public int BaseUpsertEntryZapZap(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            driveMainIndexRecord.identityId = _identityId;

            lock (_dbLock)
            {
                using (var conn = this.CreateDisposableConnection())
                {
                    int n = 0;
                    conn.CreateCommitUnitOfWork(() =>
                    {
                        n = tblDriveMainIndex.Upsert(conn, driveMainIndexRecord);

                        tblDriveAclIndex.DeleteAllRows(conn, _identityId, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
                        tblDriveAclIndex.InsertRows(conn, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
                        tblDriveTagIndex.DeleteAllRows(conn, _identityId, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
                        tblDriveTagIndex.InsertRows(conn, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

                        // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                        //
                    });

                    return n;
                }
            }
        }


        public int DeleteEntry(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            lock (_dbLock)
            {
                int n = 0;
                conn.CreateCommitUnitOfWork(() =>
                {
                    tblDriveAclIndex.DeleteAllRows(conn, _identityId, driveId, fileId);
                    tblDriveTagIndex.DeleteAllRows(conn, _identityId, driveId, fileId);
                    n = tblDriveMainIndex.Delete(conn, _identityId, driveId, fileId);
                });
                return n;
            }
        }



        public int BaseUpdateEntryZapZap(DatabaseConnection conn,
            DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            driveMainIndexRecord.identityId = _identityId;

            lock (_dbLock)
            {
                int n = 0;
                conn.CreateCommitUnitOfWork(() =>
                {
                    n = tblDriveMainIndex.Update(conn, driveMainIndexRecord);

                    tblDriveAclIndex.DeleteAllRows(conn, _identityId, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
                    tblDriveAclIndex.InsertRows(conn, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
                    tblDriveTagIndex.DeleteAllRows(conn, _identityId, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
                    tblDriveTagIndex.InsertRows(conn, driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

                    // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                    //
                });

                return n;
            }
        }


        /// <summary>
        /// Only kept to not change all tests! Do not use.
        /// </summary>
        public void AddEntryPassalongToUpsert(Guid driveId, Guid fileId,
            Guid? globalTransitId,
            Int32 fileType,
            Int32 dataType,
            string senderId,
            Guid? groupId,
            Guid? uniqueId,
            Int32 archivalStatus,
            UnixTimeUtc userDate,
            Int32 requiredSecurityGroup,
            List<Guid> accessControlList,
            List<Guid> tagIdList,
            Int64 byteCount,
            Int32 fileSystemType = (int)FileSystemType.Standard,
            Int32 fileState = 0)
        {
            if (byteCount < 1)
                throw new ArgumentException("byteCount must be at least 1");

            lock (_dbLock)
            {
                using (var conn = this.CreateDisposableConnection())
                {
                    var r = new DriveMainIndexRecord()
                    {
                        driveId = driveId,
                        fileId = fileId,
                        globalTransitId = globalTransitId,
                        fileState = fileState,
                        userDate = userDate,
                        fileType = fileType,
                        dataType = dataType,
                        senderId = senderId,
                        groupId = groupId,
                        uniqueId = uniqueId,
                        archivalStatus = archivalStatus,
                        historyStatus = 0,
                        requiredSecurityGroup = requiredSecurityGroup,
                        fileSystemType = fileSystemType,
                        byteCount = byteCount,
                        hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                        hdrVersionTag = SequentialGuid.CreateGuid(),
                        hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrTransferStatus = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                        hdrTmpDriveType = SequentialGuid.CreateGuid()
                    };
                    BaseUpsertEntryZapZap(r, accessControlList: accessControlList, tagIdList: tagIdList);
                }
            }
        }
        /// <summary>
        /// Only kept to not change all tests! Do not use.
        /// </summary>
        public int UpdateEntryZapZapPassAlong(DatabaseConnection conn, Guid driveId, Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            string senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            Int64? byteCount = null,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null,
            Int32 fileSystemType = 0)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            lock (_dbLock)
            {
                int n = 0;

                conn.CreateCommitUnitOfWork(() =>
                {
                    var r = new DriveMainIndexRecord()
                    {
                        driveId = driveId,
                        fileId = fileId,
                        globalTransitId = globalTransitId,
                        fileState = fileState ?? 0,
                        userDate = userDate ?? UnixTimeUtc.ZeroTime,
                        fileType = fileType ?? 0,
                        dataType = dataType ?? 0,
                        senderId = senderId,
                        groupId = groupId,
                        uniqueId = uniqueId,
                        archivalStatus = archivalStatus ?? 0,
                        historyStatus = 0,
                        requiredSecurityGroup = requiredSecurityGroup ?? 999,
                        fileSystemType = fileSystemType,
                        byteCount = byteCount ?? 1,
                        hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                        hdrVersionTag = SequentialGuid.CreateGuid(),
                        hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrTransferStatus = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                        hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                        hdrTmpDriveType = SequentialGuid.CreateGuid()
                    };
                    BaseUpdateEntryZapZap(conn: conn, r, accessControlList: accessControlList, tagIdList: tagIdList);
                });

                return n;
            }
        }
        private string SharedWhereAnd(List<string> listWhere, IntRange requiredSecurityGroup, List<Guid> aclAnyOf, List<int> filetypesAnyOf,
            List<int> datatypesAnyOf, List<Guid> globalTransitIdAnyOf, List<Guid> uniqueIdAnyOf, List<Guid> tagsAnyOf,
            List<Int32> archivalStatusAnyOf,
            List<string> senderidAnyOf,
            List<Guid> groupIdAnyOf,
            UnixTimeUtcRange userdateSpan,
            List<Guid> tagsAllOf,
            Int32? fileSystemType,
            Guid driveId)
        {
            string leftJoin = "";

            listWhere.Add($"driveMainIndex.identityId = x'{Convert.ToHexString(_identityId.ToByteArray())}'");
            listWhere.Add($"driveMainIndex.driveid = x'{Convert.ToHexString(driveId.ToByteArray())}'");
            listWhere.Add($"(fileSystemType == {fileSystemType})");
            listWhere.Add($"(requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End})");

            //
            // An ACL for a file is the required security group and optional list of circles
            //   This means that we first check for the security group, must match
            //   We then also check if EITHER there is a circle matching anyOf the circles provided
            //   OR if there are no circles defined for the fileId in question (the NOT IN check).
            //
            if (IsSet(aclAnyOf))
            {
                leftJoin = $"LEFT JOIN driveAclIndex cir ON (driveMainIndex.identityId = cir.identityId AND driveMainIndex.driveId = cir.driveId AND driveMainIndex.fileId = cir.fileId)";


                listWhere.Add($"(  (cir.fileId IS NULL) OR cir.aclMemberId IN ({HexList(aclAnyOf)})  )");

                // Alternative working solution. Drop the LEFT JOIN and instead do this.
                // I think that the LEFT JOIN will be more efficient, but not fully sure.
                //
                //listWhere.Add($"(((fileid IN (SELECT DISTINCT fileId FROM driveAclIndex WHERE aclmemberid IN ({HexList(aclAnyOf)}))) OR " +
                //               "(fileId NOT IN (SELECT DISTINCT fileId FROM driveAclIndex WHERE driveMainIndex.fileId = driveAclIndex.fileId))))");
            }

            if (IsSet(filetypesAnyOf))
            {
                listWhere.Add($"filetype IN ({IntList(filetypesAnyOf)})");
            }

            if (IsSet(datatypesAnyOf))
            {
                listWhere.Add($"datatype IN ({IntList(datatypesAnyOf)})");
            }

            if (IsSet(globalTransitIdAnyOf))
            {
                listWhere.Add($"globaltransitid IN ({HexList(globalTransitIdAnyOf)})");
            }

            if (IsSet(uniqueIdAnyOf))
            {
                listWhere.Add($"uniqueid IN ({HexList(uniqueIdAnyOf)})");
            }

            if (IsSet(tagsAnyOf))
            {
                listWhere.Add($"driveMainIndex.fileid IN (SELECT DISTINCT fileid FROM drivetagindex WHERE drivetagindex.identityId=driveMainIndex.identityId AND tagId IN ({HexList(tagsAnyOf)}))");
            }

            if (IsSet(archivalStatusAnyOf))
            {
                listWhere.Add($"archivalStatus IN ({IntList(archivalStatusAnyOf)})");
            }

            if (IsSet(senderidAnyOf))
            {
                listWhere.Add($"senderid IN ({UnsafeStringList(senderidAnyOf)})");
            }

            if (IsSet(groupIdAnyOf))
            {
                listWhere.Add($"groupid IN ({HexList(groupIdAnyOf)})");
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();
                listWhere.Add($"(userdate >= {userdateSpan.Start.milliseconds} AND userdate <= {userdateSpan.End.milliseconds})");
            }

            if (IsSet(tagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                listWhere.Add($"{AndIntersectHexList(tagsAllOf)}");
            }

            return leftJoin;
        }


        /// <summary>
        /// Get a page with up to 'noOfItems' rows in either newest first or oldest first order as
        /// specified by the 'newestFirstOrder' bool. If the cursor.stopAtBoundary is null, paging will
        /// continue until the last data row. If set, the paging will stop at the specified point. 
        /// For example if you wanted to get all the latest items but stop
        /// at 2023-03-15, set the stopAtBoundary to this value by constructing a cusor with the 
        /// appropriate constructor (by time or by fileId).
        /// </summary>
        /// <param name="driveId">The drive you're querying</param>
        /// <param name="noOfItems">Maximum number of results you want back</param>
        /// <param name="cursor">Pass null to get a complete set of data. Continue to pass the cursor to get the next page. pagingCursor will be updated. When no more data is available, pagingCursor is set to null (query will restart if you keep passing it)</param>
        /// <param name="newestFirstOrder">true to get pages from the newest item first, false to get pages from the oldest item first.</param>
        /// <param name="fileIdSort">true to order by fileId, false to order by usedDate, fileId</param>
        /// <param name="requiredSecurityGroup"></param>
        /// <param name="filetypesAnyOf"></param>
        /// <param name="datatypesAnyOf"></param>
        /// <param name="senderidAnyOf"></param>
        /// <param name="groupIdAnyOf"></param>
        /// <param name="userdateSpan"></param>
        /// <param name="aclAnyOf"></param>
        /// <param name="tagsAnyOf"></param>
        /// <param name="tagsAllOf"></param>
        /// <returns>List of fileIds in the dataset, and indicates if there is more data to fetch.</fileId></returns>
        public (List<Guid>, bool moreRows) QueryBatch(DatabaseConnection conn, Guid driveId,
            int noOfItems,
            ref QueryBatchCursor cursor,
            bool newestFirstOrder,
            bool fileIdSort = true,
            Int32? fileSystemType = (int)FileSystemType.Standard,
            List<int> fileStateAnyOf = null,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<string> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            if (null == fileSystemType)
            {
                throw new OdinSystemException("fileSystemType required in Query Batch");
            }

            if (noOfItems < 1)
            {
                throw new OdinSystemException("Must QueryBatch() no less than one item.");
            }

            if (cursor == null)
            {
                cursor = new QueryBatchCursor();
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }


            //
            // Set order for appropriate direction
            //
            char sign;
            char isign;
            string direction;

            if (newestFirstOrder)
            {
                sign = '<';
                isign = '>';
                direction = "DESC";
            }
            else
            {
                sign = '>';
                isign = '<';
                direction = "ASC";
            }

            var listWhereAnd = new List<string>();

            if (cursor.pagingCursor != null)
            {
                if (fileIdSort)
                    listWhereAnd.Add($"driveMainIndex.fileid {sign} x'{Convert.ToHexString(cursor.pagingCursor)}'");
                else
                {
                    if (cursor.userDatePagingCursor == null)
                        throw new Exception("userDatePagingCursor cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add(
                        $"((userDate = {cursor.userDatePagingCursor.Value.milliseconds} AND driveMainIndex.fileid {sign} x'{Convert.ToHexString(cursor.pagingCursor)}') OR (userDate {sign} {cursor.userDatePagingCursor.Value.milliseconds}))");
                }
            }

            if (cursor.stopAtBoundary != null)
            {
                if (fileIdSort)
                    listWhereAnd.Add($"driveMainIndex.fileid {isign} x'{Convert.ToHexString(cursor.stopAtBoundary)}'");
                else
                {
                    if (cursor.userDateStopAtBoundary == null)
                        throw new Exception("userDateStopAtBoundary cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add(
                        $"((userDate = {cursor.userDateStopAtBoundary.Value.milliseconds} AND driveMainIndex.fileid {isign} x'{Convert.ToHexString(cursor.stopAtBoundary)}') OR (userDate {isign} {cursor.userDateStopAtBoundary.Value.milliseconds}))");
                }
            }

            string leftJoin = SharedWhereAnd(listWhereAnd, requiredSecurityGroup, aclAnyOf, filetypesAnyOf, datatypesAnyOf, globalTransitIdAnyOf,
                uniqueIdAnyOf, tagsAnyOf, archivalStatusAnyOf, senderidAnyOf, groupIdAnyOf, userdateSpan, tagsAllOf,
                fileSystemType, driveId);

            if (IsSet(fileStateAnyOf))
            {
                listWhereAnd.Add($"fileState IN ({IntList(fileStateAnyOf)})");
            }

            string selectOutputFields;
            if (fileIdSort)
                selectOutputFields = "driveMainIndex.fileId";
            else
                selectOutputFields = "driveMainIndex.fileId, userDate";

            string order;
            if (fileIdSort)
            {
                order = "driveMainIndex.fileId " + direction;
            }
            else
            {
                order = "userDate " + direction + ", driveMainIndex.fileId " + direction;
            }

            // Read +1 more than requested to see if we're at the end of the dataset
            string stm = $"SELECT DISTINCT {selectOutputFields} FROM driveMainIndex {leftJoin} WHERE " + string.Join(" AND ", listWhereAnd) + $" ORDER BY {order} LIMIT {noOfItems + 1}";
            using (var cmd = CreateCommand())
            {
                cmd.CommandText = stm;

                lock (conn._lock)
                {
                    using (var rdr = conn.ExecuteReader(cmd, CommandBehavior.Default))
                    {
                        var result = new List<Guid>();
                        var _fileId = new byte[16];
                        long _userDate = 0;

                        int i = 0;
                        while (rdr.Read())
                        {
                            rdr.GetBytes(0, 0, _fileId, 0, 16);
                            result.Add(new Guid(_fileId));

                            if (fileIdSort == false)
                                _userDate = rdr.GetInt64(1);

                            i++;
                            if (i >= noOfItems)
                                break;
                        }

                        if (i > 0)
                        {
                            cursor.pagingCursor = _fileId; // The last result, ought to be a lone copy
                            if (fileIdSort == false)
                                cursor.userDatePagingCursor = new UnixTimeUtc(_userDate);
                        }

                        bool HasMoreRows = rdr.Read(); // Unfortunately, this seems like the only way to know if there's more rows

                        return (result, HasMoreRows);
                    } // using rdr
                } // lock
            } // using command
        }


        /// <summary>
        /// Will get the newest item first as specified by the cursor.
        /// </summary>
        /// <param name="driveId">Drive ID</param>
        /// <param name="noOfItems">Maximum number of results you want back</param>
        /// <param name="cursor">Pass null to get a complete set of data. Continue to pass the cursor to get the next page.</param>
        /// <param name="requiredSecurityGroup"></param>
        /// <param name="filetypesAnyOf"></param>
        /// <param name="datatypesAnyOf"></param>
        /// <param name="senderidAnyOf"></param>
        /// <param name="groupIdAnyOf"></param>
        /// <param name="userdateSpan"></param>
        /// <param name="aclAnyOf"></param>
        /// <param name="tagsAnyOf"></param>
        /// <param name="tagsAllOf"></param>
        /// <returns></returns>
        public (List<Guid>, bool moreRows) QueryBatchAuto(DatabaseConnection conn, Guid driveId,
            int noOfItems,
            ref QueryBatchCursor cursor,
            Int32? fileSystemType = (int)FileSystemType.Standard,
            List<int> fileStateAnyOf = null,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<string> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            bool pagingCursorWasNull = ((cursor == null) || (cursor.pagingCursor == null));

            var (result, moreRows) =
                QueryBatch(conn, driveId, noOfItems,
                    ref cursor,
                    newestFirstOrder: true,
                    fileIdSort: true,
                    fileSystemType,
                    fileStateAnyOf,
                    requiredSecurityGroup,
                    globalTransitIdAnyOf,
                    filetypesAnyOf,
                    datatypesAnyOf,
                    senderidAnyOf,
                    groupIdAnyOf,
                    uniqueIdAnyOf,
                    archivalStatusAnyOf,
                    userdateSpan,
                    aclAnyOf,
                    tagsAnyOf,
                    tagsAllOf);

            //
            // OldToNew:
            //   nextBoundaryCursor and currentBoundaryCursor not needed.
            //   PagingCursor will probably suffice
            // 

            if (result.Count > 0)
            {
                // If pagingCursor is null, it means we are getting a the newest data,
                // and since we got a dataset back then we need to set the nextBoundaryCursor for this first set
                //
                if (pagingCursorWasNull)
                    cursor.nextBoundaryCursor = result[0].ToByteArray(); // Set to the newest cursor

                if (result.Count < noOfItems)
                {
                    if (moreRows == false) // Advance the cursor
                    {
                        if (cursor.nextBoundaryCursor != null)
                        {
                            cursor.stopAtBoundary = cursor.nextBoundaryCursor;
                            cursor.nextBoundaryCursor = null;
                            cursor.pagingCursor = null;
                        }
                        else
                        {
                            cursor.nextBoundaryCursor = null;
                            cursor.pagingCursor = null;
                        }
                    }

                    // If we didn't get all the items that were requested and there is no more data, then we
                    // need to be sure there is no more data in the next data set. 
                    // The API contract says that if you receive less than the requested
                    // items then there is no more data.
                    //
                    // Do a recursive call to check there are no more items.
                    //
                    var (r2, moreRows2) = QueryBatchAuto(conn, driveId, noOfItems - result.Count, ref cursor,
                        fileSystemType,
                        fileStateAnyOf,
                        requiredSecurityGroup,
                        globalTransitIdAnyOf,
                        filetypesAnyOf,
                        datatypesAnyOf,
                        senderidAnyOf,
                        groupIdAnyOf,
                        uniqueIdAnyOf,
                        archivalStatusAnyOf,
                        userdateSpan,
                        aclAnyOf,
                        tagsAnyOf,
                        tagsAllOf);

                    // There was more data
                    if (r2.Count > 0)
                    {
                        // The r2 result set should be newer than the result set
                        r2.AddRange(result);
                        return (r2, moreRows2);
                    }
                }
            }
            else
            {
                if (cursor.nextBoundaryCursor != null)
                {
                    cursor.stopAtBoundary = cursor.nextBoundaryCursor;
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                    return QueryBatchAuto(conn, driveId, noOfItems, ref cursor,
                        fileSystemType,
                        fileStateAnyOf,
                        requiredSecurityGroup,
                        globalTransitIdAnyOf,
                        filetypesAnyOf,
                        datatypesAnyOf,
                        senderidAnyOf,
                        groupIdAnyOf,
                        uniqueIdAnyOf,
                        archivalStatusAnyOf,
                        userdateSpan,
                        aclAnyOf, tagsAnyOf, tagsAllOf);
                }
                else
                {
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                }
            }

            return (result, moreRows);
        }

        /// <summary>
        /// Will fetch all items that have been modified as defined by the cursors. The oldest modified item will be returned first.
        /// </summary>
        /// 
        /// <param name="noOfItems">Maximum number of rows you want back</param>
        /// <param name="cursor">Set to null to get any item ever modified. Keep passing.</param>
        /// <param name="stopAtModifiedUnixTimeSeconds">Optional. If specified won't get items older than this parameter.</param>
        /// <param name="startFromCursor">Start from the supplied cursor fileId, use null to start at the beginning.</param>
        /// <returns></returns>
        public (List<Guid>, bool moreRows) QueryModified(DatabaseConnection conn, Guid driveId, int noOfItems,
            ref UnixTimeUtcUnique cursor,
            UnixTimeUtcUnique stopAtModifiedUnixTimeSeconds = default(UnixTimeUtcUnique),
            Int32? fileSystemType = (int)FileSystemType.Standard,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<string> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            if (conn.db != this)
                throw new ArgumentException("connection and database object mismatch");

            if (null == fileSystemType)
            {
                throw new OdinSystemException("fileSystemType required in Query Modified");
            }

            if (noOfItems < 1)
            {
                throw new OdinSystemException("Must QueryModified() no less than one item.");
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            var listWhereAnd = new List<string>();

            listWhereAnd.Add($"modified > {cursor.uniqueTime}");

            if (stopAtModifiedUnixTimeSeconds.uniqueTime > 0)
            {
                listWhereAnd.Add($"modified >= {stopAtModifiedUnixTimeSeconds.uniqueTime}");
            }

            string leftJoin = SharedWhereAnd(listWhereAnd, requiredSecurityGroup, aclAnyOf, filetypesAnyOf, datatypesAnyOf, globalTransitIdAnyOf,
                uniqueIdAnyOf, tagsAnyOf, archivalStatusAnyOf, senderidAnyOf, groupIdAnyOf, userdateSpan, tagsAllOf,
                fileSystemType, driveId);

            string stm = $"SELECT DISTINCT driveMainIndex.fileid, modified FROM drivemainindex {leftJoin} WHERE " + string.Join(" AND ", listWhereAnd) + $" ORDER BY modified ASC LIMIT {noOfItems + 1}";
            using (var cmd = CreateCommand())
            {
                cmd.CommandText = stm;

                lock (conn._lock)
                {
                    using (var rdr = conn.ExecuteReader(cmd, CommandBehavior.Default))
                    {
                        var result = new List<Guid>();
                        var fileId = new byte[16];

                        int i = 0;
                        long ts = 0;

                        while (rdr.Read())
                        {
                            rdr.GetBytes(0, 0, fileId, 0, 16);
                            result.Add(new Guid(fileId));
                            ts = rdr.GetInt64(1);
                            i++;
                            if (i >= noOfItems)
                                break;
                        }

                        if (i > 0)
                            cursor = new UnixTimeUtcUnique(ts);

                        return (result, rdr.Read());
                    } // using rdr
                } // lock
            } // using command
        }


        private string IntList(List<int> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"{list[i]}";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }

        private string HexList(List<byte[]> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"x'{Convert.ToHexString(list[i])}'";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }

        private string HexList(List<Guid> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"x'{Convert.ToHexString(list[i].ToByteArray())}'";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }
        
        private string UnsafeStringList(List<string> list)
        {
            // WARNING! This does not do anything to escape the strings.
            // It's up to the caller to ensure the strings are safe.
            return string.Join(", ", list.Select(item => "'" + item + "'"));
        }

        /// <summary>
        /// Returns true if the list should be used in the query
        /// </summary>
        private bool IsSet(List<byte[]> list)
        {
            return list?.Count > 0;
        }

        private bool IsSet(List<Guid> list)
        {
            return list?.Count > 0;
        }

        private bool IsSet(List<int> list)
        {
            return list?.Count > 0;
        }

        private bool IsSet(List<string> list)
        {
            return list?.Count > 0;
        }

        private string AndIntersectHexList(List<Guid> list)
        {
            int len = list.Count;
            string s = "";

            if (len < 1)
                throw new Exception("AllOf list must have at least two entries");


            // Alternative (but complicated):
            // How to select ONE matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE tagid=x'189820F6018B51349CC07ED86B02C8F6';
            // How to select TWO matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE fileid in (SELECT DISTINCT fileid FROM tagindex WHERE tagid=x'189820F6018B51349CC07ED86B02C8F6') and tagid=x'189820F6018C218FA0F0F18E86139565';
            // How to select TRHEE matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE fileid in (SELECT DISTINCT fileid FROM tagindex WHERE fileid IN(SELECT DISTINCT fileid FROM tagindex WHERE tagid = x'189820F6018C218FA0F0F18E86139565') AND tagid = x'189820F6018B51349CC07ED86B02C8F6') and tagid = x'189820F6018C7F083F50CFCD32AF2B7F';
            //

            s = $"driveMainIndex.fileid IN (SELECT DISTINCT fileid FROM drivetagindex WHERE tagid= x'{Convert.ToHexString(list[0].ToByteArray())}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM drivetagindex WHERE tagid= x'{Convert.ToHexString(list[i].ToByteArray())}' ";
            }

            s += ") ";

            return s;
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