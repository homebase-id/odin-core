﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using System.Xml;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public class MainIndexMeta(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        IdentityKey identityKey,
        TableDriveAclIndex driveAclIndex,
        TableDriveTagIndex driveTagIndex,
        TableDriveLocalTagIndex driveLocalTagIndex,
        TableDriveMainIndex driveMainIndex)
    {
        private readonly DatabaseType _databaseType = scopedConnectionFactory.DatabaseType;
        private static readonly string selectOutputFields;
        public TableDriveLocalTagIndex _driveLocalTagIndex = driveLocalTagIndex;

        static MainIndexMeta()
        {
            // Initialize selectOutputFields statically
            selectOutputFields = string.Join(",",
                TableDriveMainIndex.GetColumnNames()
                    .Where(name => !name.Equals("identityId", StringComparison.OrdinalIgnoreCase)
                                && !name.Equals("driveId", StringComparison.OrdinalIgnoreCase))
                    .Select(name => name.Equals("fileId", StringComparison.OrdinalIgnoreCase) ? "driveMainIndex.fileId" :
                                    name.Equals("rowId", StringComparison.OrdinalIgnoreCase) ? "driveMainIndex.rowId" :
                                    name));

            
        }

        public async Task<int> DeleteEntryAsync(Guid driveId, Guid fileId)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            await driveAclIndex.DeleteAllRowsAsync(driveId, fileId);
            await driveTagIndex.DeleteAllRowsAsync(driveId, fileId);
            n = await driveMainIndex.DeleteAsync(driveId, fileId);

            tx.Commit();
            return n;
        }

        /// <summary>
        /// By design does NOT update the TransferHistory and ReactionSummary fields, even when 
        /// they are specified in the record.
        /// </summary>
        /// <param name="driveMainIndexRecord"></param>
        /// <param name="accessControlList"></param>
        /// <param name="tagIdList"></param>
        /// <returns></returns>
        public async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            driveMainIndexRecord.identityId = identityKey;

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            n = await driveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord);

            await driveAclIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveAclIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
            await driveTagIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveTagIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

            // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
            //

            tx.Commit();

            return n;
        }


        public async Task<int> BaseUpdateEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            driveMainIndexRecord.identityId = identityKey;

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            n = await driveMainIndex.UpdateAsync(driveMainIndexRecord);

            await driveAclIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveAclIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
            await driveTagIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveTagIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

            // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
            //

            tx.Commit();

            return n;
        }

        private string SharedWhereAnd(List<string> listWhere, IntRange requiredSecurityGroup, List<Guid> aclAnyOf, List<int> filetypesAnyOf,
            List<int> datatypesAnyOf, List<Guid> globalTransitIdAnyOf, List<Guid> uniqueIdAnyOf, List<Guid> tagsAnyOf, List<Guid> localTagsAnyOf,
            List<Int32> archivalStatusAnyOf,
            List<string> senderidAnyOf,
            List<Guid> groupIdAnyOf,
            UnixTimeUtcRange userdateSpan,
            List<Guid> tagsAllOf,
            List<Guid> localTagsAllOf,
            Int32? fileSystemType,
            Guid driveId)
        {
            var leftJoin = "";

            listWhere.Add($"driveMainIndex.identityId = {identityKey.BytesToSql(_databaseType)}");
            listWhere.Add($"driveMainIndex.driveid = {driveId.BytesToSql(_databaseType)}");
            listWhere.Add($"(fileSystemType = {fileSystemType})");
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

            if (IsSet(localTagsAnyOf))
            {
                listWhere.Add($"driveMainIndex.fileId IN (SELECT DISTINCT fileId FROM driveLocalTagIndex WHERE driveLocalTagIndex.identityId=driveMainIndex.identityId AND TagId IN ({HexList(localTagsAnyOf)}))");
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
                listWhere.Add($"{AndIntersectHexList(tagsAllOf, "driveTagIndex")}");
            }

            if (IsSet(localTagsAllOf))
            {
                // TODO: This will return 0 matches. Figure out the right query.
                listWhere.Add($"{AndIntersectHexList(localTagsAllOf, "driveLocalTagIndex")}");
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
        public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchAsync(Guid driveId,
            int noOfItems,
            QueryBatchCursor cursor,
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
            List<Guid> tagsAllOf = null,
            List<Guid> localTagsAnyOf = null,
            List<Guid> localTagsAllOf = null)
        {
            if (null == fileSystemType)
            {
                throw new OdinSystemException("fileSystemType required in Query Batch");
            }

            if (noOfItems < 1)
            {
                throw new OdinSystemException("Must QueryBatch() no less than one item.");
            }

            if (noOfItems == int.MaxValue)
            {
                noOfItems--;
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
                    listWhereAnd.Add($"driveMainIndex.fileid {sign} {cursor.pagingCursor.ToSql(_databaseType)}");
                else
                {
                    if (cursor.userDatePagingCursor == null)
                        throw new Exception("userDatePagingCursor cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add(
                        $"((userDate = {cursor.userDatePagingCursor.Value.milliseconds} AND driveMainIndex.fileid {sign} {cursor.pagingCursor.ToSql(_databaseType)}) OR (userDate {sign} {cursor.userDatePagingCursor.Value.milliseconds}))");
                }
            }

            if (cursor.stopAtBoundary != null)
            {
                if (fileIdSort)
                    listWhereAnd.Add($"driveMainIndex.fileid {isign} {cursor.stopAtBoundary.ToSql(_databaseType)}");
                else
                {
                    if (cursor.userDateStopAtBoundary == null)
                        throw new Exception("userDateStopAtBoundary cannot be null, cursor initialized incorrectly");

                    listWhereAnd.Add(
                        $"((userDate = {cursor.userDateStopAtBoundary.Value.milliseconds} AND driveMainIndex.fileid {isign} {cursor.stopAtBoundary.ToSql(_databaseType)}) OR (userDate {isign} {cursor.userDateStopAtBoundary.Value.milliseconds}))");
                }
            }

            if (IsSet(fileStateAnyOf))
            {
                listWhereAnd.Add($"fileState IN ({IntList(fileStateAnyOf)})");
            }

            string leftJoin = SharedWhereAnd(listWhereAnd, requiredSecurityGroup, aclAnyOf, filetypesAnyOf, datatypesAnyOf, globalTransitIdAnyOf,
                uniqueIdAnyOf, tagsAnyOf, localTagsAnyOf, archivalStatusAnyOf, senderidAnyOf, groupIdAnyOf, userdateSpan, tagsAllOf, localTagsAllOf,
                fileSystemType, driveId);

            // string selectOutputFields    = "driveMainIndex.fileId, globalTransitId, fileState, requiredSecurityGroup, fileSystemType, userDate, fileType, dataType, archivalStatus, historyStatus, senderId, groupId, uniqueId, byteCount, hdrEncryptedKeyHeader, hdrVersionTag, hdrAppData, hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary, hdrServerData, hdrTransferHistory, hdrFileMetaData, hdrTmpDriveAlias, hdrTmpDriveType, created, modified";
            // string selectOutputFields = "driveMainIndex.fileId, globalTransitId, fileState, requiredSecurityGroup, fileSystemType, userDate, fileType, dataType, archivalStatus, historyStatus, senderId, groupId, uniqueId, byteCount, hdrEncryptedKeyHeader, hdrVersionTag, hdrAppData,                                    hdrReactionSummary, hdrServerData, hdrTransferHistory, hdrFileMetaData, hdrTmpDriveAlias, hdrTmpDriveType, created, modified";
            /*if (fileIdSort)
                selectOutputFields = "driveMainIndex.fileId";
            else
                selectOutputFields = "driveMainIndex.fileId, userDate";*/

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
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = stm;
            using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
            {
                var result = new List<DriveMainIndexRecord>();
                byte[] _fileId = null;
                long _userDate = 0;

                int i = 0;
                while (await rdr.ReadAsync())
                {
                    var r = driveMainIndex.ReadAllColumns(rdr, driveId);
                    _fileId = r.fileId.ToByteArray();

                    result.Add(r); // XXX

                    if (fileIdSort == false)
                        _userDate = r.userDate.milliseconds;

                    i++;
                    if (i >= noOfItems)
                        break;
                }

                if (i > 0)
                { 
                    if (_fileId == null) throw new Exception("impossible");
                    cursor.pagingCursor = _fileId; // The last result, ought to be a lone copy
                    if (fileIdSort == false)
                        cursor.userDatePagingCursor = new UnixTimeUtc(_userDate);
                }

                bool hasMoreRows = await rdr.ReadAsync(); // Unfortunately, this seems like the only way to know if there's more rows

                return (result, hasMoreRows, cursor);
            } // using rdr
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
        public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchAutoAsync(Guid driveId,
            int noOfItems,
            QueryBatchCursor cursor,
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
            List<Guid> tagsAllOf = null,
            List<Guid> localTagsAnyOf = null,
            List<Guid> localTagsAllOf = null)
        {
            bool pagingCursorWasNull = ((cursor == null) || (cursor.pagingCursor == null));

            var (result, moreRows, refCursor) = await
                QueryBatchAsync(driveId, noOfItems,
                    cursor,
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
                    tagsAllOf,
                    localTagsAnyOf,
                    localTagsAllOf);

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
                    refCursor.nextBoundaryCursor = result[0].fileId.ToByteArray(); // Set to the newest cursor

                if (result.Count < noOfItems)
                {
                    if (moreRows == false) // Advance the cursor
                    {
                        if (refCursor.nextBoundaryCursor != null)
                        {
                            refCursor.stopAtBoundary = refCursor.nextBoundaryCursor;
                            refCursor.nextBoundaryCursor = null;
                            refCursor.pagingCursor = null;
                        }
                        else
                        {
                            refCursor.nextBoundaryCursor = null;
                            refCursor.pagingCursor = null;
                        }
                    }

                    // If we didn't get all the items that were requested and there is no more data, then we
                    // need to be sure there is no more data in the next data set. 
                    // The API contract says that if you receive less than the requested
                    // items then there is no more data.
                    //
                    // Do a recursive call to check there are no more items.
                    //
                    var (r2, moreRows2, refCursor2) = await QueryBatchAutoAsync(driveId, noOfItems - result.Count, refCursor,
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
                        tagsAllOf,
                        localTagsAnyOf,
                        localTagsAllOf);

                    // There was more data
                    if (r2.Count > 0)
                    {
                        // The r2 result set should be newer than the result set
                        r2.AddRange(result);
                        return (r2, moreRows2, refCursor2);
                    }
                }
            }
            else
            {
                if (refCursor.nextBoundaryCursor != null)
                {
                    refCursor.stopAtBoundary = refCursor.nextBoundaryCursor;
                    refCursor.nextBoundaryCursor = null;
                    refCursor.pagingCursor = null;
                    return await QueryBatchAutoAsync(driveId, noOfItems, refCursor,
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
                        tagsAnyOf, tagsAllOf,
                        localTagsAnyOf, localTagsAllOf);
                }
                else
                {
                    refCursor.nextBoundaryCursor = null;
                    refCursor.pagingCursor = null;
                }
            }

            return (result, moreRows, refCursor);
        }


        public static string CreateModifiedCursor(UnixTimeUtc? utc, long? rowId)
        {
            UnixTimeUtc c = UnixTimeUtc.ZeroTime;

            if (utc == null)
                return null;
            else 
                return utc!.ToString() + "," + rowId!.ToString();
        }

        /// <summary>
        /// Cursor format is a string of "timestamp,rowid" but old cursors will just be "timestamp" with no ",rowid"
        /// </summary>
        /// <returns>true if parsed successfully, if false, return longs are null if can't be parsed</returns>
        public static bool TryParseModifiedCursor(string cursor, out UnixTimeUtc? timestamp, out long? rowId)
        {
            timestamp = null;
            rowId = null;

            if (cursor == null)
                return false;

            var parts = cursor.Split(',');

            if (parts.Length == 1 && long.TryParse(parts[0], out long ts))
            {
                // This section is only for "old" cursors
                // Old cursors are in UnixTimeUtcUnique, so make them into a UnixTimeUtc
                if (ts > 1L << 42)
                    timestamp = ts >> 16;  
                return true;
            }
            else if (parts.Length == 2 &&
                     long.TryParse(parts[0], out long ts2) &&
                     long.TryParse(parts[1], out long parsedRowId))
            {
                timestamp = ts2;
                rowId = parsedRowId;
                return true;
            }

            return false;
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
        public async Task<(List<DriveMainIndexRecord>, bool moreRows, string cursor)> QueryModifiedAsync(Guid driveId, int noOfItems,
            string cursor,
            UnixTimeUtc stopAtModifiedUnixTimeSeconds = default(UnixTimeUtc),
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
            List<Guid> tagsAllOf = null,
            List<Guid> localTagsAnyOf = null,
            List<Guid> localTagsAllOf = null)
        {
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

            TryParseModifiedCursor(cursor, out var modifiedTimeCursor, out var rowIdCursor);

            if (modifiedTimeCursor == null)
                modifiedTimeCursor = 0;

            if (rowIdCursor == null)
                rowIdCursor = 0;

            listWhereAnd.Add($"(modified, driveMainIndex.rowId) > ({modifiedTimeCursor}, {rowIdCursor})");

            if (stopAtModifiedUnixTimeSeconds.milliseconds > 0)
            {
                listWhereAnd.Add($"modified >= {stopAtModifiedUnixTimeSeconds.milliseconds}");
            }

            string leftJoin = SharedWhereAnd(listWhereAnd, requiredSecurityGroup, aclAnyOf, filetypesAnyOf, datatypesAnyOf, globalTransitIdAnyOf,
                uniqueIdAnyOf, tagsAnyOf, localTagsAnyOf, archivalStatusAnyOf, senderidAnyOf, groupIdAnyOf, userdateSpan, tagsAllOf, localTagsAllOf,
                fileSystemType, driveId);

            string stm = $"SELECT DISTINCT {selectOutputFields} FROM driveMainIndex {leftJoin} WHERE " + string.Join(" AND ", listWhereAnd) + $" ORDER BY modified ASC, driveMainIndex.rowId ASC LIMIT {noOfItems + 1}";

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = stm;

            using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
            {
                var result = new List<DriveMainIndexRecord>();
                byte[] _fileId = null;

                int i = 0;
                long ts = 0;
                long rid = 0;

                while (await rdr.ReadAsync())
                {
                    var r = driveMainIndex.ReadAllColumns(rdr, driveId);
                    _fileId = r.fileId.ToByteArray();
                    result.Add(r);
                    ts = (long) r.modified?.milliseconds; // XXX
                    rid = (long)r.rowId;
                    i++;
                    if (i >= noOfItems)
                        break;
                }

                if (i > 0) // Should the cursor be set to null if there are no results!?
                    cursor =  MainIndexMeta.CreateModifiedCursor(ts, rid);

                return (result, await rdr.ReadAsync(), cursor);
            } // using rdr
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
                s +=list[i].ToSql(scopedConnectionFactory.DatabaseType);

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
                s += list[i].BytesToSql(scopedConnectionFactory.DatabaseType);

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

        private string AndIntersectHexList(List<Guid> list, string tableName)
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

            s = $"driveMainIndex.fileid IN (SELECT DISTINCT fileid FROM {tableName} WHERE tagid = {list[0].BytesToSql(_databaseType)} ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM {tableName} WHERE tagid = {list[i].BytesToSql(_databaseType)} ";
            }

            s += ") ";

            return s;
        }


        //
        // THESE ARE HERE FOR LEGACY REASONS FOR TESTING JUST BECAUSE I'M
        // TOO LAZY TO REWRITE THE TESTS
        //

        /// <summary>
        /// Only kept to not change all tests! Do not use.
        /// </summary>
        internal async Task AddEntryPassalongToUpsertAsync(Guid driveId, Guid fileId,
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
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid()
            };
            await BaseUpsertEntryZapZapAsync(r, accessControlList: accessControlList, tagIdList: tagIdList);
        }


        /// <summary>
        /// Only kept to not change all tests! Do not use.
        /// </summary>
        internal async Task<int> UpdateEntryZapZapPassAlongAsync(Guid driveId, Guid fileId,
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
            int n = 0;
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
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid()
            };
            await BaseUpdateEntryZapZapAsync(r, accessControlList: accessControlList, tagIdList: tagIdList);

            return n;
        }
    }
}