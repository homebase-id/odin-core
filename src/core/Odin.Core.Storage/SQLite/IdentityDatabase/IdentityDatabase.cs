using Odin.Core.Exceptions;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.Data;
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
    public static class ReservedFileTypes
    {
        private const int Start = 2100000000;
        private const int End = int.MaxValue;

        public const int CommandMessage = 2100000001;

        public static bool IsInReservedRange(int value)
        {
            return value is < End and >= Start;
        }
    }

    public class IdentityDatabase : DatabaseBase
    {
        public class NullableGuid { public Guid? uniqueId; }

        // Drive tables
        public readonly TableDriveMainIndex tblDriveMainIndex = null;
        public readonly TableDriveAclIndex  tblDriveAclIndex = null;
        public readonly TableDriveTagIndex  tblDriveTagIndex = null;
        public readonly TableDriveCommandMessageQueue tblDriveCommandMessageQueue = null;
        public readonly TableDriveReactions tblDriveReactions = null;

        // Identity tables
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
        public readonly TableAppNotifications tblAppNotificationsTable = null;

        public readonly string CN;

        public readonly CacheHelper _cache = new CacheHelper("identity");
        private readonly string _file;
        private readonly int _line;

        public IdentityDatabase(string connectionString, long commitFrequencyMs = 5000, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString, commitFrequencyMs)
        {
            // Drive
            tblDriveMainIndex = new TableDriveMainIndex(this, _cache);
            tblDriveAclIndex = new TableDriveAclIndex(this, _cache);
            tblDriveTagIndex = new TableDriveTagIndex(this, _cache);
            tblDriveCommandMessageQueue = new TableDriveCommandMessageQueue(this, _cache);
            tblDriveReactions = new TableDriveReactions(this, _cache);

            // Identity
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
            tblAppNotificationsTable = new TableAppNotifications(this, _cache);

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

            // Drives
            tblDriveMainIndex.Dispose();
            tblDriveAclIndex.Dispose();
            tblDriveTagIndex.Dispose();
            tblDriveCommandMessageQueue.Dispose();
            tblDriveReactions.Dispose();

            // Identity
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
            tblAppNotificationsTable.Dispose();

            base.Dispose();
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override void CreateDatabase(bool dropExistingTables = true)
        {
            // Drives
            tblDriveMainIndex.EnsureTableExists(dropExistingTables);
            tblDriveAclIndex.EnsureTableExists(dropExistingTables);
            tblDriveTagIndex.EnsureTableExists(dropExistingTables);
            tblDriveCommandMessageQueue.EnsureTableExists(dropExistingTables);
            tblDriveReactions.EnsureTableExists(dropExistingTables);

            // Identity
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
            tblAppNotificationsTable.EnsureTableExists(dropExistingTables);

            if (dropExistingTables)
                Vacuum();
        }

        /// <summary>
        /// If a transaction is not already ongoing, then the three tables are updated in a single transaction.
        /// Otherwise they'll just be put into the existing transaction.
        /// </summary>
        /// <param name="driveId">The drive ID</param>
        /// <param name="fileId">The GUID file ID</param>
        /// <param name="fileType">An int32 designating the local drive file type, e.g. "attribute" (application specific)</param>
        /// <param name="dataType">An int32 designating the data type of the file, e.g. "full name" (application specific)</param>
        /// <param name="senderId">Who sent this item (may be null)</param>
        /// <param name="groupId">The group id, may be NULL, e.g. for conversations thread, blog comments, email thread, picture album</param>
        /// <param name="userDate">An int64 designating the user date (GetZeroTime(dt) or GetZeroTimeSeconds())</param>
        /// <param name="requiredSecurityGroup">The security group required </param>
        /// <param name="accessControlList">The list of Id's of the circles or identities which can access this file</param>
        /// <param name="tagIdList">The tags</param>
        public void AddEntry(Guid driveId, Guid fileId,
            Guid? globalTransitId,
            Int32 fileType,
            Int32 dataType,
            byte[] senderId,
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

            using (CreateCommitUnitOfWork())
            {
                tblDriveMainIndex.Insert(new DriveMainIndexRecord() { driveId = driveId, fileId = fileId, globalTransitId = globalTransitId, fileState = fileState, userDate = userDate, fileType = fileType, dataType = dataType, senderId = senderId.ToString(), groupId = groupId, uniqueId = uniqueId, archivalStatus = archivalStatus, historyStatus = 0, requiredSecurityGroup = requiredSecurityGroup, fileSystemType = fileSystemType, byteCount = byteCount });
                tblDriveAclIndex.InsertRows(driveId, fileId, accessControlList);
                tblDriveTagIndex.InsertRows(driveId, fileId, tagIdList);
            }
        }

        public void DeleteEntry(Guid driveId, Guid fileId)
        {
            using (CreateCommitUnitOfWork())
            {
                tblDriveAclIndex.DeleteAllRows(driveId, fileId);
                tblDriveTagIndex.DeleteAllRows(driveId, fileId);
                tblDriveMainIndex.Delete(driveId, fileId);
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntry(Guid driveId, Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            Int64? byteCount = null,
            List<Guid> addAccessControlList = null,
            List<Guid> deleteAccessControlList = null,
            List<Guid> addTagIdList = null,
            List<Guid> deleteTagIdList = null)
        {
            using (CreateCommitUnitOfWork())
            {

                tblDriveMainIndex.UpdateRow(driveId, fileId, globalTransitId: globalTransitId, fileState: fileState, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, new IdentityDatabase.NullableGuid() { uniqueId = uniqueId }, archivalStatus: archivalStatus, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup, byteCount: byteCount);

                tblDriveAclIndex.InsertRows(driveId, fileId, addAccessControlList);
                tblDriveTagIndex.InsertRows(driveId, fileId, addTagIdList);
                tblDriveAclIndex.DeleteRow(driveId, fileId, deleteAccessControlList);
                tblDriveTagIndex.DeleteRow(driveId, fileId, deleteTagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntryZapZap(Guid driveId, Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
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
            using (CreateCommitUnitOfWork())
            {
                tblDriveMainIndex.UpdateRow(driveId, fileId, globalTransitId: globalTransitId, fileState: fileState, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, new IdentityDatabase.NullableGuid() { uniqueId = uniqueId }, archivalStatus: archivalStatus, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup, byteCount: byteCount);

                tblDriveAclIndex.DeleteAllRows(driveId, fileId);
                tblDriveAclIndex.InsertRows(driveId, fileId, accessControlList);
                tblDriveTagIndex.DeleteAllRows(driveId, fileId);
                tblDriveTagIndex.InsertRows(driveId, fileId, tagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
            }
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
        public (List<Guid>, bool moreRows) QueryBatch(Guid driveId, 
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
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
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
                    listWhereAnd.Add($"fileid {sign} x'{Convert.ToHexString(cursor.pagingCursor)}'");
                else
                {
                    if (cursor.userDatePagingCursor == null)
                        throw new Exception("userDatePagingCursor cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add($"((userDate = {cursor.userDatePagingCursor.Value.milliseconds} AND fileid {sign} x'{Convert.ToHexString(cursor.pagingCursor)}') OR (userDate {sign} {cursor.userDatePagingCursor.Value.milliseconds}))");
                }
            }

            if (cursor.stopAtBoundary != null)
            {
                if (fileIdSort)
                    listWhereAnd.Add($"fileid {isign} x'{Convert.ToHexString(cursor.stopAtBoundary)}'");
                else
                {
                    if (cursor.userDateStopAtBoundary == null)
                        throw new Exception("userDateStopAtBoundary cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add($"((userDate = {cursor.userDateStopAtBoundary.Value.milliseconds} AND fileid {isign} x'{Convert.ToHexString(cursor.stopAtBoundary)}') OR (userDate {isign} {cursor.userDateStopAtBoundary.Value.milliseconds}))");
                }
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            if (fileSystemType == null)
            {
                throw new Exception($"{nameof(fileSystemType)} is required");
            }

            listWhereAnd.Add($"(fileSystemType == {fileSystemType})");

            if (aclAnyOf == null)
            {
                listWhereAnd.Add($"(requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End})");
            }
            else
            {
                listWhereAnd.Add($"((requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) AND " +
                            $"(fileid IN (SELECT DISTINCT fileid FROM driveaclindex WHERE aclmemberid IN ({HexList(aclAnyOf)}))))");
            }

            if (IsSet(fileStateAnyOf))
            {
                listWhereAnd.Add($"fileState IN ({IntList(fileStateAnyOf)})");
            }

            if (IsSet(globalTransitIdAnyOf))
            {
                listWhereAnd.Add($"globaltransitid IN ({HexList(globalTransitIdAnyOf)})");
            }

            if (IsSet(filetypesAnyOf))
            {
                listWhereAnd.Add($"filetype IN ({IntList(filetypesAnyOf)})");
            }

            if (IsSet(datatypesAnyOf))
            {
                listWhereAnd.Add($"datatype IN ({IntList(datatypesAnyOf)})");
            }

            if (IsSet(archivalStatusAnyOf))
            {
                listWhereAnd.Add($"archivalStatus IN ({IntList(archivalStatusAnyOf)})");
            }

            if (IsSet(senderidAnyOf))
            {
                listWhereAnd.Add($"senderid IN ({HexList(senderidAnyOf)})");
            }

            if (IsSet(groupIdAnyOf))
            {
                listWhereAnd.Add($"groupid IN ({HexList(groupIdAnyOf)})");
            }

            if (IsSet(uniqueIdAnyOf))
            {
                listWhereAnd.Add($"uniqueid IN ({HexList(uniqueIdAnyOf)})");
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();
                listWhereAnd.Add($"(userdate >= {userdateSpan.Start.milliseconds} AND userdate <= {userdateSpan.End.milliseconds})");
            }

            if (IsSet(tagsAnyOf))
            {
                listWhereAnd.Add($"fileid IN (SELECT DISTINCT fileid FROM drivetagindex WHERE tagid IN ({HexList(tagsAnyOf)}))");
            }

            if (IsSet(tagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                listWhereAnd.Add($"{AndHexList(tagsAllOf)}");
            }

            string strWhere = $"WHERE driveid=x'{Convert.ToHexString(driveId.ToByteArray())}'";
            if (listWhereAnd.Count > 0)
            {
                strWhere += " AND " + string.Join(" AND ", listWhereAnd);
            }

            string selectOutputFields;
            if (fileIdSort)
                selectOutputFields = "fileId";
            else
                selectOutputFields = "fileId, userDate";

            string order;
            if (fileIdSort)
            {
                order = "fileId " + direction;
            }
            else
            {
                order = "userDate " + direction + ", fileId " + direction;
            }

            // Read +1 more than requested to see if we're at the end of the dataset
            string stm = $"SELECT {selectOutputFields} FROM drivemainindex " + strWhere + $" ORDER BY {order} LIMIT {noOfItems + 1}";

            var cmd = this.CreateCommand();
            cmd.CommandText = stm;

            var rdr = this.ExecuteReader(cmd, CommandBehavior.Default);

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
        public (List<Guid>, bool moreRows) QueryBatchAuto(Guid driveId, 
            int noOfItems,
            ref QueryBatchCursor cursor,
            Int32? fileSystemType = (int)FileSystemType.Standard,
            List<int> fileStateAnyOf = null,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            bool pagingCursorWasNull = ((cursor == null) || (cursor.pagingCursor == null));

            var (result, moreRows) =
                QueryBatch(driveId, noOfItems,
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
                    var (r2, moreRows2) = QueryBatchAuto(driveId, noOfItems - result.Count, ref cursor,
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
                    return QueryBatchAuto(driveId, noOfItems, ref cursor,
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
        public (List<Guid>, bool moreRows) QueryModified(Guid driveId, int noOfItems,
            ref UnixTimeUtcUnique cursor,
            UnixTimeUtcUnique stopAtModifiedUnixTimeSeconds = default(UnixTimeUtcUnique),
            Int32? fileSystemType = (int)FileSystemType.Standard,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            List<Int32> archivalStatusAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            string stm;
            string strWhere = $"WHERE driveId = x'{Convert.ToHexString(driveId.ToByteArray())}' AND ";

            if (null == fileSystemType)
            {
                throw new OdinSystemException("fileSystemType required in Query Modified");
            }

            strWhere += $"modified > {cursor.uniqueTime} ";

            if (stopAtModifiedUnixTimeSeconds.uniqueTime > 0)
            {
                strWhere += $"AND modified >= {stopAtModifiedUnixTimeSeconds.uniqueTime} ";
            }

            if (strWhere != "")
                strWhere += "AND ";

            strWhere += $"(fileSystemType == {fileSystemType})";

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            strWhere += $"AND (requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) ";

            if (IsSet(filetypesAnyOf))
            {
                strWhere += $"AND filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (IsSet(datatypesAnyOf))
            {
                strWhere += $"AND datatype IN ({IntList(datatypesAnyOf)}) ";
            }

            if (IsSet(archivalStatusAnyOf))
            {
                strWhere += $"AND archivalStatus IN ({IntList(archivalStatusAnyOf)}) ";
            }

            if (IsSet(senderidAnyOf))
            {
                strWhere += $"AND senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (IsSet(groupIdAnyOf))
            {
                strWhere += $"AND groupid IN ({HexList(groupIdAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan.Start.milliseconds} AND userdate <= {userdateSpan.End.milliseconds}) ";
            }

            if (IsSet(globalTransitIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"globaltransitid IN ({HexList(globalTransitIdAnyOf)}) ";
            }

            if (IsSet(uniqueIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"uniqueid IN ({HexList(uniqueIdAnyOf)}) ";
            }

            if (IsSet(tagsAnyOf))
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM drivetagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            // if (IsSet(aclAnyOf))
            // {
            //     strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmemberid IN ({HexList(aclAnyOf)})) ";
            // }

            if (IsSet(aclAnyOf))
            {
                strWhere += $"AND ((requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) OR " +
                                 $"(fileid IN (SELECT DISTINCT fileid FROM driveaclindex WHERE aclmemberid IN ({HexList(aclAnyOf)}))))";
            }
            else
            {
                strWhere += $"AND (requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End})";
            }

            if (IsSet(tagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                strWhere += $"AND {AndHexList(tagsAllOf)} ";
            }

            stm = $"SELECT fileid, modified FROM drivemainindex " + strWhere + $" ORDER BY modified ASC LIMIT {noOfItems + 1}";

            var cmd = this.CreateCommand();
            cmd.CommandText = stm;

            var rdr = this.ExecuteReader(cmd, CommandBehavior.Default);

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

        /// <summary>
        /// Returns true if the list should be used in the query
        /// </summary>
        private bool IsSet(List<byte[]> list)
        {
            return list != null && list.Any();
        }

        private bool IsSet(List<Guid> list)
        {
            return list != null && list.Any();
        }

        private bool IsSet(List<int> list)
        {
            return list != null && list.Any();
        }

        private string AndHexList(List<Guid> list)
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

            s = $"fileid IN (SELECT DISTINCT fileid FROM drivetagindex WHERE tagid= x'{Convert.ToHexString(list[0].ToByteArray())}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM drivetagindex WHERE tagid= x'{Convert.ToHexString(list[i].ToByteArray())}' ";
            }

            s += ") ";

            return s;
        }

        private string AndHexList(List<byte[]> list)
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

            s = $"fileid IN (SELECT DISTINCT fileid drivetagindex WHERE tagid= x'{Convert.ToHexString(list[0])}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM drivetagindex WHERE tagid= x'{Convert.ToHexString(list[i])}' ";
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