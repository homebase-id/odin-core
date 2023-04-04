using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using Youverse.Core.Exceptions;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    /// <summary>
    /// Database types:
    /// 
    /// TimeSeries - E.g. chat or mail (sequence never changes)
    ///    Clustered by fileId. Ordered by fileId. 
    ///    Path is yyyy/mm/dd
    ///    
    /// Random     - E.g. cloud drive (no particular ordering for the user)
    ///    Clustered by RowId. Ordered by RowId. indexed by lastModified. 
    ///    Path is X1/X2 where X is a Hex digit and represent the last (random) byte of the guid.
    ///    
    /// </summary>
    public enum DatabaseIndexKind
    {
        TimeSeries,
        Random,
    }

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

    public class DriveDatabase : DatabaseBase
    {
        private DatabaseIndexKind _kind;

        public readonly TableMainIndex TblMainIndex = null;
        public readonly TableAclIndex TblAclIndex = null;
        public readonly TableTagIndex TblTagIndex = null;
        public readonly TableReactions TblReactions = null;
        public readonly TableCommandMessageQueue TblCmdMsgQueue = null;


        public DriveDatabase(string connectionString, DatabaseIndexKind databaseKind, long commitFrequencyMs = 5000) : base(connectionString, commitFrequencyMs)
        {
            _kind = databaseKind;

            TblMainIndex = new TableMainIndex(this);
            TblAclIndex = new TableAclIndex(this);
            TblTagIndex = new TableTagIndex(this);
            TblCmdMsgQueue = new TableCommandMessageQueue(this);
            TblReactions = new TableReactions(this);
        }

        ~DriveDatabase()
        {
        }


        public override void Dispose()
        {
            Commit();

            TblMainIndex?.Dispose();
            TblAclIndex?.Dispose();
            TblTagIndex?.Dispose();
            TblCmdMsgQueue?.Dispose();
            TblReactions?.Dispose();

            base.Dispose();
        }

        public DatabaseIndexKind GetKind()
        {
            return _kind;
        }


        public override void CreateDatabase(bool dropExistingTables = true)
        {
            TblMainIndex.EnsureTableExists(dropExistingTables);
            TblAclIndex.EnsureTableExists(dropExistingTables);
            TblTagIndex.EnsureTableExists(dropExistingTables);
            TblCmdMsgQueue.EnsureTableExists(dropExistingTables);
            TblReactions.EnsureTableExists(dropExistingTables);
            Vacuum();
        }


        /// <summary>
        /// If a transaction is not already ongoing, then the three tables are updated in a single transaction.
        /// Otherwise they'll just be put into the existing transaction.
        /// </summary>
        /// <param name="fileId">The GUID file ID</param>
        /// <param name="fileType">An int32 designating the local drive file type, e.g. "attribute" (application specific)</param>
        /// <param name="dataType">An int32 designating the data type of the file, e.g. "full name" (application specific)</param>
        /// <param name="senderId">Who sent this item (may be null)</param>
        /// <param name="groupId">The group id, may be NULL, e.g. for conversations thread, blog comments, email thread, picture album</param>
        /// <param name="userDate">An int64 designating the user date (GetZeroTime(dt) or GetZeroTimeSeconds())</param>
        /// <param name="requiredSecurityGroup">The security group required </param>
        /// <param name="accessControlList">The list of Id's of the circles or identities which can access this file</param>
        /// <param name="tagIdList">The tags</param>
        public void AddEntry(Guid fileId,
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
            Int32 fileSystemType = (int)FileSystemType.Standard
        )
        {
            using (CreateCommitUnitOfWork())
            {
                TblMainIndex.Insert(new MainIndexRecord() { fileId = fileId, globalTransitId = globalTransitId, userDate = userDate,  fileType = fileType,  dataType = dataType, senderId = senderId.ToString(), groupId = groupId, uniqueId = uniqueId, archivalStatus = archivalStatus, historyStatus = 0, requiredSecurityGroup = requiredSecurityGroup, fileSystemType = fileSystemType });
                TblAclIndex.InsertRows(fileId, accessControlList);
                TblTagIndex.InsertRows(fileId, tagIdList);
            }
        }

        public void DeleteEntry(Guid fileId)
        {
            using (CreateCommitUnitOfWork())
            {
                TblAclIndex.DeleteAllRows(fileId);
                TblTagIndex.DeleteAllRows(fileId);
                TblMainIndex.Delete(fileId);
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntry(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Guid> addAccessControlList = null,
            List<Guid> deleteAccessControlList = null,
            List<Guid> addTagIdList = null,
            List<Guid> deleteTagIdList = null)
        {
            using (CreateCommitUnitOfWork())
            {
                TblMainIndex.UpdateRow(fileId, globalTransitId: globalTransitId, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, uniqueId: uniqueId, archivalStatus: archivalStatus, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.InsertRows(fileId, addAccessControlList);
                TblTagIndex.InsertRows(fileId, addTagIdList);
                TblAclIndex.DeleteRow(fileId, deleteAccessControlList);
                TblTagIndex.DeleteRow(fileId, deleteTagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntryZapZap(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null,
            Int32 fileSystemType = 0)
        {
            using (CreateCommitUnitOfWork())
            {
                TblMainIndex.UpdateRow(fileId, globalTransitId: globalTransitId, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, uniqueId: uniqueId, archivalStatus: archivalStatus, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.DeleteAllRows(fileId);
                TblAclIndex.InsertRows(fileId, accessControlList);
                TblTagIndex.DeleteAllRows(fileId);
                TblTagIndex.InsertRows(fileId, tagIdList);

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
        /// <param name="noOfItems">Maximum number of results you want back</param>
        /// <param name="cursor">Pass null to get a complete set of data. Continue to pass the cursor to get the next page. pagingCursor will be updated. When no more data is available, pagingCursor is set to null (query will restart if you keep passing it)</param>
        /// <param name="newestFirstOrder">true to get pages from the newest item first, false to get pages from the oldest item first.</param>
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
        public (List<Guid>, bool moreRows) QueryBatch(int noOfItems,
            ref QueryBatchCursor cursor,
            bool newestFirstOrder,
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
            if (null == fileSystemType)
            {
                throw new YouverseSystemException("fileSystemType required in Query Batch");
            }

            if (noOfItems < 1)
            {
                throw new YouverseSystemException("Must QueryBatch() no less than one item.");
            }

            if (cursor == null)
            {
                cursor = new QueryBatchCursor();
            }

            //
            // OPPOSITE DIRECTION CHANGES...
            // For opposite direction, reverse < to > and > to < and 
            // DESC to ASC
            //

            var listWhereAnd = new List<string>();

            if (cursor.pagingCursor != null)
            {
                if (newestFirstOrder)
                    listWhereAnd.Add($"fileid < x'{Convert.ToHexString(cursor.pagingCursor)}'");
                else
                    listWhereAnd.Add($"fileid > x'{Convert.ToHexString(cursor.pagingCursor)}'");
            }

            if (cursor.stopAtBoundary != null)
            {
                if (newestFirstOrder)
                    listWhereAnd.Add($"fileid > x'{Convert.ToHexString(cursor.stopAtBoundary)}'");
                else
                    listWhereAnd.Add($"fileid < x'{Convert.ToHexString(cursor.stopAtBoundary)}'");
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
                listWhereAnd.Add($"((requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) OR " +
                            $"(fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmemberid IN ({HexList(aclAnyOf)}))))");
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
                listWhereAnd.Add($"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)}))");
            }

            if (IsSet(tagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                listWhereAnd.Add($"{AndHexList(tagsAllOf)}");
            }

            string strWhere = "";
            if (listWhereAnd.Count > 0)
            {
                strWhere = " WHERE " + string.Join(" AND ", listWhereAnd);
            }

            string order;
            if (newestFirstOrder)
                order = "DESC";
            else
                order = "ASC";

            // Read +1 more than requested to see if we're at the end of the dataset
            string stm = $"SELECT fileid FROM mainindex" + strWhere + $" ORDER BY fileid {order} LIMIT {noOfItems + 1}";

            var cmd = this.CreateCommand();
            cmd.CommandText = stm;

            var rdr = this.ExecuteReader(cmd, CommandBehavior.Default);

            var result = new List<Guid>();
            var _fileId = new byte[16];

            int i = 0;
            while (rdr.Read())
            {
                rdr.GetBytes(0, 0, _fileId, 0, 16);
                result.Add(new Guid(_fileId));
                i++;
                if (i >= noOfItems)
                    break;
            }

            if (i > 0)
                cursor.pagingCursor = _fileId; // The last result, ought to be a lone copy

            bool HasMoreRows = rdr.Read(); // Unfortunately, this seems like the only way to know if there's more rows

            return (result, HasMoreRows);
        }


        /// <summary>
        /// Will get the newest item first as specified by the cursor.
        /// </summary>
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
        public List<Guid> QueryBatchAuto(int noOfItems,
            ref QueryBatchCursor cursor,
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
            bool pagingCursorWasNull = ((cursor == null) || (cursor.pagingCursor == null));
            
            var (result, moreRows) = 
                QueryBatch(noOfItems, 
                              ref cursor,
                              newestFirstOrder: true,
                              fileSystemType,
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
                    var r2 = QueryBatchAuto(noOfItems - result.Count, ref cursor,
                        fileSystemType,
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
                        return r2;
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
                    return QueryBatchAuto(noOfItems, ref cursor, 
                        fileSystemType, 
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

            return result;
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
        public List<Guid> QueryModified(int noOfItems,
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
            Stopwatch stopWatch = new Stopwatch();

            string stm;
            string strWhere = "";


            if (null == fileSystemType)
            {
                throw new YouverseSystemException("fileSystemType required in Query Modified");
            }
            
            stopWatch.Start();
            
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
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (IsSet(aclAnyOf))
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmemberid IN ({HexList(aclAnyOf)})) ";
            }

            if (IsSet(tagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                strWhere += $"AND {AndHexList(tagsAllOf)} ";
            }

            if (strWhere != "")
            {
                strWhere = "WHERE " + strWhere;
            }

            stm = $"SELECT fileid, modified FROM mainindex " + strWhere + $"ORDER BY modified ASC LIMIT {noOfItems}";

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
            }

            if (i > 0)
                cursor = new UnixTimeUtcUnique(ts);

            stopWatch.Stop();
            // Utils.StopWatchStatus("QueryModified() " + stm, stopWatch);

            return result;
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

            s = $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[0].ToByteArray())}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[i].ToByteArray())}' ";
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

            s = $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[0])}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[i])}' ";
            }

            s += ") ";

            return s;
        }

        public string FileIdToPath(Guid fileid)
        {
            if (_kind == DatabaseIndexKind.TimeSeries)
            {
                UnixTimeUtc t = SequentialGuid.ToUnixTimeUtc(fileid);
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)t.milliseconds / 1000).UtcDateTime;
                return dt.Year + "/" + dt.Month.ToString("2") + "/" + dt.Day.ToString("2");
            }
            else
            {
                // Ensure even distribution
                byte[] ba = fileid.ToByteArray();
                var b1 = ba[ba.Length - 1] & (byte)0x0F;
                var b2 = (ba[ba.Length - 1] & (byte)0xF0) >> 4;

                return b2.ToString("X1") + "/" + b1.ToString("X1");
            }
        }
    }
}