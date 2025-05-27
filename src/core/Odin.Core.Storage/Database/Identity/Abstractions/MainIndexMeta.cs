using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading.Tasks;
using Minio.DataModel;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public enum QueryBatchSortField
    {
        FileId = 0, // OBSOLETE
        UserDate = 1,
        CreatedDate = 2,
        AnyChangeDate = 3, // By Newly created or modified
        OnlyModifiedDate = 4 // Not yet implemented
    }

    public enum QueryBatchSortOrder
    {
        Default = 0, // NewestFirst
        NewestFirst = 1,
        OldestFirst = 2
    }


    public class MainIndexMeta(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        OdinIdentity odinIdentity,
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
        /// <param name="useThisNewVersionTag"></param>
        /// <returns></returns>
        public async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null,
            Guid? useThisNewVersionTag = null)
        {
            driveMainIndexRecord.identityId = odinIdentity;

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            n = await driveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord, useThisNewVersionTag);

            await driveAclIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveAclIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
            await driveTagIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveTagIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

            // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
            //

            tx.Commit();

            return n;
        }

        /*
        public async Task<int> BaseUpdateEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            driveMainIndexRecord.identityId = odinIdentity;

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
        */

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

            listWhere.Add($"driveMainIndex.identityId = {odinIdentity.BytesToSql(_databaseType)}");
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
        /// Asynchronously retrieves a batch of records from the drive main index, sorted by the specified field (e.g., creation date, user date, or latest change) in the chosen order (newest or oldest first).
        /// Supports pagination via a cursor and can stop at a specified boundary. Applies various filters for precise record selection.
        /// The choice of <c>sortField</c> affects how modifications are handled; see parameter documentation for details.
        /// Returns the list of records, a boolean indicating if more rows are available, and the updated cursor for subsequent queries.
        /// </summary>
        /// <param name="driveId">The unique identifier of the drive to query.</param>
        /// <param name="noOfItems">The maximum number of records to return in this batch (must be at least 1).</param>
        /// <param name="cursor">The pagination cursor. Pass <c>null</c> to start from the beginning. The cursor is updated for subsequent queries.</param>
        /// <param name="sortOrder">The sorting order: <c>NewestFirst</c> (descending) or <c>OldestFirst</c> (ascending). Default is <c>NewestFirst</c>.</param>
        /// <param name="sortField">Determines the field used for sorting the records. The choice impacts how modifications are captured:
        ///   - <c>CreatedDate</c>: Sorts by the record's creation timestamp, which is fixed at creation. Returns the latest version of a record at query time, but subsequent modifications do not change its sort order, so updated records won't reappear in later pages.
        ///   - <c>UserDate</c>: Sorts by the client-specified date, typically fixed. Returns the latest version of a record at query time, but subsequent modifications do not alter its sort order unless <c>userDate</c> is updated, so updated records typically won't reappear in later pages.
        ///   - <c>AnyChangeDate</c>: Sorts by the most recent change (latest of <c>modified</c> or <c>created</c>), ensuring both new and updated records are included in the order of their last change.
        ///   - <c>OnlyModifiedDate</c>: (Not yet implemented) Will sort only by the modification date, excluding newly created records.
        ///   For <c>UserDate</c> or <c>CreatedDate</c>, clients may need to separately query for modifications using <c>OnlyModifiedDate</c> (once implemented) to capture updated records, which could result in duplicates or require additional logic to handle updates.
        /// </param>
        /// <returns>A tuple containing the list of <c>DriveMainIndexRecord</c>, a boolean indicating if more rows are available, and the updated <c>QueryBatchCursor</c>.</returns>
        /// <example>
        /// To fetch 10 records sorted by their latest change (newest first) before March 15, 2023, from a drive with a specific security group:
        /// <code>
        /// var boundaryInstant = NodaTime.Instant.FromDateTimeUtc(new DateTime(2023, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        /// var cursor = new QueryBatchCursor { stopAtBoundary = new TimeRowCursor(new UnixTimeUtc(boundaryInstant), null) };
        /// var (records, moreRows, updatedCursor) = await QueryBatchAsync(driveId: Guid.Parse("drive-id"), 
        ///     noOfItems: 10, cursor: cursor, sortOrder: QueryBatchSortOrder.NewestFirst, 
        ///     sortField: QueryBatchSortField.AnyChangeDate, fileSystemType: (int)FileSystemType.Standard, 
        ///     requiredSecurityGroup: new IntRange(1, 5));
        /// </code>
        /// This example uses <c>AnyChangeDate</c> to retrieve records based on their most recent change, ensuring both new and modified records are included.
        /// </example>
        public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchAsync(Guid driveId,
        int noOfItems,
            QueryBatchCursor cursor,
            QueryBatchSortOrder sortOrder = QueryBatchSortOrder.NewestFirst,
            QueryBatchSortField sortField = QueryBatchSortField.CreatedDate,
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

            if ((sortOrder == QueryBatchSortOrder.NewestFirst) || (sortOrder == QueryBatchSortOrder.Default))
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

            string timeField;
            var listWhereAnd = new List<string>();

            if ((sortField == QueryBatchSortField.CreatedDate) || (sortField == QueryBatchSortField.FileId))
                timeField = "created";
            else if (sortField == QueryBatchSortField.UserDate)
                timeField = "userDate";
            else if (sortField == QueryBatchSortField.AnyChangeDate)
            {
                // IMPORTANT: When querying by modified and getting a cursor back, it is important we do not
                // query for the last millisecond, otherwise we risk getting a cursor with a millisecond that
                // might in parallel get an update on another row in the database with the same timestamp, and
                // in such a case, that record would be missing from the dataset unless we add this less than
                // condition to the query. This means in our testing of modified cursor types we may need to
                // add delays before calling QueryBatch()
                listWhereAnd.Add($"modified < {SqlExtensions.SqlNowString(scopedConnectionFactory.DatabaseType)}");
                timeField = "modified";
            }
            else if (sortField == QueryBatchSortField.OnlyModifiedDate)
            {
                // Same important note as above
                listWhereAnd.Add($"modified < {SqlExtensions.SqlNowString(scopedConnectionFactory.DatabaseType)}");
                // When modified != created, it means it's an item that has been modified, not newly created
                listWhereAnd.Add($"modified != created"); 
                timeField = "modified";
            }
            else
                throw new Exception("Invalid sorting value");

            if (cursor.pagingCursor != null)
            {
                if (cursor.pagingCursor.rowId == null)
                {
                    if (sortOrder == QueryBatchSortOrder.NewestFirst)
                        cursor.pagingCursor.rowId = long.MaxValue;
                    else
                        cursor.pagingCursor.rowId = 0;
                }

                listWhereAnd.Add($"({timeField}, driveMainIndex.rowId) {sign} ({cursor.pagingCursor.Time.milliseconds}, {cursor.pagingCursor.rowId})");
            }

            if (cursor.stopAtBoundary != null)
            {
                if (cursor.stopAtBoundary.rowId == null)
                {
                    if (sortOrder == QueryBatchSortOrder.NewestFirst)
                        cursor.stopAtBoundary.rowId = long.MaxValue;
                    else
                        cursor.stopAtBoundary.rowId = 0;
                }

                listWhereAnd.Add($"({timeField}, driveMainIndex.rowId) {isign} ({cursor.stopAtBoundary.Time.milliseconds}, {cursor.stopAtBoundary.rowId})");
            }

            if (IsSet(fileStateAnyOf))
            {
                listWhereAnd.Add($"fileState IN ({IntList(fileStateAnyOf)})");
            }

            string leftJoin = SharedWhereAnd(listWhereAnd, requiredSecurityGroup, aclAnyOf, filetypesAnyOf, datatypesAnyOf, globalTransitIdAnyOf,
                uniqueIdAnyOf, tagsAnyOf, localTagsAnyOf, archivalStatusAnyOf, senderidAnyOf, groupIdAnyOf, userdateSpan, tagsAllOf, localTagsAllOf,
                fileSystemType, driveId);

            var orderString = $"{timeField} {direction}, driveMainIndex.rowId {direction}";

            // Read +1 more than requested to see if we're at the end of the dataset
            string stm = $"SELECT DISTINCT {selectOutputFields} FROM driveMainIndex {leftJoin} WHERE " + string.Join(" AND ", listWhereAnd) + $" ORDER BY {orderString} LIMIT {noOfItems + 1}";
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = stm;
            using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
            {
                var resultList = new List<DriveMainIndexRecord>();

                int i = 0;
                DriveMainIndexRecord record = null;

                while (await rdr.ReadAsync())
                {
                    record = driveMainIndex.ReadAllColumns(rdr, driveId);

                    resultList.Add(record);

                    i++;
                    if (i >= noOfItems)
                        break;
                }

                if (i > 0)
                {
                    if (sortField == QueryBatchSortField.UserDate)
                        cursor.pagingCursor = new TimeRowCursor(record.userDate, record.rowId);
                    else if (sortField == QueryBatchSortField.AnyChangeDate || sortField == QueryBatchSortField.OnlyModifiedDate)
                        cursor.pagingCursor = new TimeRowCursor(record.modified, record.rowId);
                    else if (sortField == QueryBatchSortField.FileId || sortField == QueryBatchSortField.CreatedDate)
                        cursor.pagingCursor = new TimeRowCursor(record.created, record.rowId);
                    else
                        throw new OdinSystemException("Invalid QueryBatchSortField type");
                }

                bool hasMoreRows = await rdr.ReadAsync(); // Unfortunately, this seems like the only way to know if there's more rows

                return (resultList, hasMoreRows, cursor);
            } // using rdr
        }

        /// <summary>
        /// Asynchronously retrieves a batch of records from the drive main index, sorted by the specified field in the chosen order (newest or oldest first).
        /// For `NewestFirst`, it automatically manages pagination with smart cursor handling, using boundary markers and recursive queries to ensure all records up to the initial newest point are fetched, even with new record additions.
        /// For `OldestFirst`, it delegates to `QueryBatchAsync` for simple, static pagination. Supports various filters for precise record selection.
        /// Returns a tuple containing the list of records, a boolean indicating if more rows are available, and the updated cursor for subsequent queries.
        /// <example>
        /// To fetch 10 newest records created before March 15, 2023, from a drive with a specific security group:
        /// <code>
        /// var boundaryInstant = NodaTime.Instant.FromDateTimeUtc(new DateTime(2023, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        /// var cursor = new QueryBatchCursor { stopAtBoundary = new TimeRowCursor(new UnixTimeUtc(boundaryInstant), null) };
        /// var (records, moreRows, updatedCursor) = await QueryBatchSmartCursorAsync(driveId: Guid.Parse("drive-id"), 
        ///     noOfItems: 10, cursor: cursor, sortOrder: QueryBatchSortOrder.NewestFirst, 
        ///     sortField: QueryBatchSortField.CreatedDate, fileSystemType: (int)FileSystemType.Standard, 
        ///     requiredSecurityGroup: new IntRange(1, 5));
        /// </code>
        /// </example>
        /// </summary>
        /// <returns></returns>

        public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchSmartCursorAsync(Guid driveId,
            int noOfItems,
            QueryBatchCursor cursor,
            QueryBatchSortOrder sortOrder = QueryBatchSortOrder.NewestFirst,
            QueryBatchSortField sortField = QueryBatchSortField.CreatedDate,
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
                    cursor: cursor,
                    sortOrder: sortOrder,
                    sortField: sortField,
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

            if (sortOrder == QueryBatchSortOrder.OldestFirst)
                return (result, moreRows, refCursor);

            if (result.Count > 0)
            {
                // If pagingCursor is null, it means we are getting a the newest data,
                // and since we got a dataset back then we need to set the nextBoundaryCursor for this first set
                //
                if (pagingCursorWasNull)
                {
                    // Set to the newest cursor 
                    if ((sortField == QueryBatchSortField.CreatedDate) || (sortField == QueryBatchSortField.FileId))
                        refCursor.nextBoundaryCursor = new TimeRowCursor(result[0].created, result[0].rowId);
                    else if (sortField == QueryBatchSortField.AnyChangeDate)
                        refCursor.nextBoundaryCursor = new TimeRowCursor(result[0].modified, result[0].rowId);
                    else if (sortField == QueryBatchSortField.UserDate)
                        refCursor.nextBoundaryCursor = new TimeRowCursor(result[0].userDate, result[0].rowId);
                    else if (sortField == QueryBatchSortField.OnlyModifiedDate)
                        refCursor.nextBoundaryCursor = new TimeRowCursor(result[0].modified, result[0].rowId);
                    else
                        throw new OdinSystemException("Illegal QueryBatchSortField type");
                }

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
                    var (r2, moreRows2, refCursor2) = await QueryBatchSmartCursorAsync(driveId, noOfItems - result.Count, cursor: refCursor,
                        sortOrder: sortOrder,
                        sortField: sortField,
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
                    return await QueryBatchSmartCursorAsync(driveId, noOfItems, cursor: refCursor,
                        sortOrder: sortOrder,
                        sortField: sortField,
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


        /// <summary>
        /// Will fetch all items that have been modified as defined by the cursors. The oldest modified item will be returned first.
        /// </summary>
        /// 
        /// <param name="noOfItems">Maximum number of rows you want back</param>
        /// <param name="cursorString">Set to null to get any item ever modified. Keep passing.</param>
        /// <param name="stopAtModifiedUnixTimeSeconds">Optional. If specified won't get items older than this parameter.</param>
        /// <param name="startFromCursor">Start from the supplied cursor fileId, use null to start at the beginning.</param>
        /// <returns></returns>
        public async Task<(List<DriveMainIndexRecord>, bool moreRows, string cursor)> QueryModifiedAsync(Guid driveId, int noOfItems,
            string cursorString,
            TimeRowCursor stopAtModifiedUnixTimeSeconds = null,
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

            if (noOfItems == int.MaxValue)
            {
                noOfItems--;
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            var cursor = TimeRowCursor.FromJsonOrOldString(cursorString);

            if (cursor == null)
                cursor = new TimeRowCursor(0, 0);

            if (cursor.rowId == null)
                cursor.rowId = 0;

            var listWhereAnd = new List<string>();

            listWhereAnd.Add($"(modified, driveMainIndex.rowId) > ({cursor.Time}, {cursor.rowId})");

            if (stopAtModifiedUnixTimeSeconds != null)
            {
                // You can argue if it should be < or <= but important that stopBoundary is
                // the same for QueryModified and for QueryBatch
                // Ok, we need an actual cursor with a rowid otherwise the tests will fail for
                // rows inserted on the same ms.
                if (stopAtModifiedUnixTimeSeconds.rowId == null)
                    stopAtModifiedUnixTimeSeconds.rowId = 0; // Must behave like QueryBatchAsync as well as the above rowIdCursor=0 (we're ASCending)

                listWhereAnd.Add($"(modified, driveMainIndex.rowId) < ({stopAtModifiedUnixTimeSeconds.Time.milliseconds}, {stopAtModifiedUnixTimeSeconds.rowId})");
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
                    ts = (long) r.modified.milliseconds;
                    rid = (long)r.rowId;
                    i++;
                    if (i >= noOfItems)
                        break;
                }

                if (i > 0) // Should the cursor be set to null if there are no results!?
                    cursorString = new TimeRowCursor(ts, rid).ToJson();

                return (result, await rdr.ReadAsync(), cursorString);
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
        internal async Task<(UnixTimeUtc created, UnixTimeUtc modified)> TestAddEntryPassalongToUpsertAsync(Guid driveId, Guid fileId,
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

            return (r.created, r.modified);
        }
    }
}