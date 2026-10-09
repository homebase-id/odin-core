using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

#nullable enable

// Named records (not ValueTuples) so cache entries round-trip through the default
// System.Text.Json serializer. ValueTuple's Item1/Item2/Item3 are public fields, which
// STJ skips by default; that previously serialized entries as "{}" and deserialized
// to default(...), causing an NRE in DriveQueryServiceBase.CreateClientFileHeadersAsync.
public sealed record QueryBatchCachedResult(
    List<DriveMainIndexRecord> Records,
    bool MoreRows,
    QueryBatchCursor Cursor);

public sealed record QueryModifiedCachedResult(
    List<DriveMainIndexRecord> Records,
    bool MoreRows,
    string Cursor);

//

public class QueryBatchCached : AbstractTableCaching
{
    private readonly QueryBatch _meta;
    private readonly TableDriveMainIndexCacheKeys _cacheKeys;

    public QueryBatchCached(QueryBatch meta, IIdentityTransactionalCacheFactory cacheFactory)
        : base(cacheFactory, meta.GetType().Name, TableDriveMainIndexCacheKeys.RootInvalidationTag)
    {
        _meta = meta;
        _cacheKeys = new TableDriveMainIndexCacheKeys(Cache);
    }

    private List<string> GetDriveIdInvalidationTags(Guid driveId)
    {
        return _cacheKeys.GetDriveIdInvalidationTags(driveId);
    }

    /// <summary>
    /// Whether an answer depends on the clock as well as on the data, and so must not be cached: the cache is only
    /// cleared by writes, so an identical query would keep getting it after the clock has moved on (#1905).
    /// A change-date query holds back rows with modified at or after now. Newest first those are the first rows, so
    /// any answer may be missing them. Oldest first they come last, and QueryBatch reports them as more rows: a
    /// short page that still has more rows is exactly an answer that held rows back. A full page is safe to cache,
    /// as anything held back sorts after it.
    /// </summary>
    private static bool DependsOnClock(QueryBatchSortOrder sortOrder, QueryBatchSortField sortField, int noOfItems,
        int recordCount, bool moreRows)
    {
        if (sortField != QueryBatchSortField.AnyChangeDate && sortField != QueryBatchSortField.OnlyModifiedDate)
        {
            return false;
        }

        if (sortOrder != QueryBatchSortOrder.OldestFirst)
        {
            return true;
        }

        return moreRows && recordCount < noOfItems;
    }


    public async Task<QueryBatchCachedResult> QueryBatchAsync(
        Guid driveId,
        int noOfItems,
        QueryBatchCursor cursor,
        QueryBatchSortOrder sortOrder = QueryBatchSortOrder.NewestFirst,
        QueryBatchSortField sortField = QueryBatchSortField.CreatedDate,
        Int32? fileSystemType = (int)FileSystemType.Standard,
        List<int>? fileStateAnyOf = null,
        IntRange? requiredSecurityGroup = null,
        List<Guid>? globalTransitIdAnyOf = null,
        List<int>? filetypesAnyOf = null,
        List<int>? datatypesAnyOf = null,
        List<string>? senderidAnyOf = null,
        List<Guid>? groupIdAnyOf = null,
        List<Guid>? uniqueIdAnyOf = null,
        List<Int32>? archivalStatusAnyOf = null,
        UnixTimeUtcRange? userdateSpan = null,
        List<Guid>? aclAnyOf = null,
        List<Guid>? tagsAnyOf = null,
        List<Guid>? tagsAllOf = null,
        List<Guid>? localTagsAnyOf = null,
        List<Guid>? localTagsAllOf = null,
        UnixTimeUtc? modifiedAfter = null,
        TimeSpan? cacheTtl = null)
    {
        var cacheKey = "QueryBatchAsync:" + driveId + ":" + HashParameters.Calculate(
            driveId,
            noOfItems,
            cursor,
            sortOrder,
            sortField,
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
            localTagsAllOf,
            modifiedAfter?.milliseconds);

        var query = async () =>
        {
            var (records, moreRows, c) = await _meta.QueryBatchAsync(
                driveId,
                noOfItems,
                cursor,
                sortOrder,
                sortField,
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
                localTagsAllOf,
                modifiedAfter);
            return new QueryBatchCachedResult(records, moreRows, c);
        };

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            EntrySize.Large,
            GetDriveIdInvalidationTags(driveId),
            storeIf: r => !DependsOnClock(sortOrder, sortField, noOfItems, r.Records.Count, r.MoreRows));

        return result;
    }

    //

    public async Task<QueryBatchCachedResult> QueryBatchSmartCursorAsync(
        Guid driveId,
        int noOfItems,
        QueryBatchCursor cursor,
        QueryBatchSortOrder sortOrder = QueryBatchSortOrder.NewestFirst,
        QueryBatchSortField sortField = QueryBatchSortField.CreatedDate,
        Int32? fileSystemType = (int)FileSystemType.Standard,
        List<int>? fileStateAnyOf = null,
        IntRange? requiredSecurityGroup = null,
        List<Guid>? globalTransitIdAnyOf = null,
        List<int>? filetypesAnyOf = null,
        List<int>? datatypesAnyOf = null,
        List<string>? senderidAnyOf = null,
        List<Guid>? groupIdAnyOf = null,
        List<Guid>? uniqueIdAnyOf = null,
        List<Int32>? archivalStatusAnyOf = null,
        UnixTimeUtcRange? userdateSpan = null,
        List<Guid>? aclAnyOf = null,
        List<Guid>? tagsAnyOf = null,
        List<Guid>? tagsAllOf = null,
        List<Guid>? localTagsAnyOf = null,
        List<Guid>? localTagsAllOf = null,
        TimeSpan? cacheTtl = null)
    {
        var cacheKey = "QueryBatchSmartCursorAsync:" + driveId + ":" + HashParameters.Calculate(
            driveId,
            noOfItems,
            cursor,
            sortOrder,
            sortField,
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

        var query = async () =>
        {
            var (records, moreRows, c) = await _meta.QueryBatchSmartCursorAsync(
                driveId,
                noOfItems,
                cursor,
                sortOrder,
                sortField,
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
            return new QueryBatchCachedResult(records, moreRows, c);
        };

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            EntrySize.Large,
            GetDriveIdInvalidationTags(driveId),
            storeIf: r => !DependsOnClock(sortOrder, sortField, noOfItems, r.Records.Count, r.MoreRows));

        return result;
    }

    //

    public async Task<QueryModifiedCachedResult> QueryModifiedAsync(
        Guid driveId,
        int noOfItems,
        string? cursorString,
        TimeRowCursor? stopAtModifiedUnixTimeSeconds = null,
        Int32? fileSystemType = (int)FileSystemType.Standard,
        IntRange? requiredSecurityGroup = null,
        List<Guid>? globalTransitIdAnyOf = null,
        List<int>? filetypesAnyOf = null,
        List<int>? datatypesAnyOf = null,
        List<string>? senderidAnyOf = null,
        List<Guid>? groupIdAnyOf = null,
        List<Guid>? uniqueIdAnyOf = null,
        List<Int32>? archivalStatusAnyOf = null,
        UnixTimeUtcRange? userdateSpan = null,
        List<Guid>? aclAnyOf = null,
        List<Guid>? tagsAnyOf = null,
        List<Guid>? tagsAllOf = null,
        List<Guid>? localTagsAnyOf = null,
        List<Guid>? localTagsAllOf = null,
        TimeSpan? cacheTtl = null)
    {
        var cacheKey = "QueryModifiedAsync:" + driveId + ":" +  HashParameters.Calculate(
            driveId,
            noOfItems,
            cursorString,
            stopAtModifiedUnixTimeSeconds,
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
            tagsAllOf,
            localTagsAnyOf,
            localTagsAllOf);

        var query = async () =>
        {
            var (records, moreRows, c) = await _meta.QueryModifiedAsync(
                driveId,
                noOfItems,
                cursorString,
                stopAtModifiedUnixTimeSeconds,
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
                tagsAllOf,
                localTagsAnyOf,
                localTagsAllOf);
            return new QueryModifiedCachedResult(records, moreRows, c);
        };

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            EntrySize.Large,
            GetDriveIdInvalidationTags(driveId),
            storeIf: r => !DependsOnClock(QueryBatchSortOrder.OldestFirst, QueryBatchSortField.OnlyModifiedDate, noOfItems,
                r.Records.Count, r.MoreRows));

        return result;
    }
}
