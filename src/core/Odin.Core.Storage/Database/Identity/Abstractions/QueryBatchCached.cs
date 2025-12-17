using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

#nullable enable

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


    public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchAsync(
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
        var cacheKey = "QueryBatchAsync:" + HashParameters.Calculate(
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

        var query = () => _meta.QueryBatchAsync(
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

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            GetDriveIdInvalidationTags(driveId));

        return result;
    }

    //

    public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchSmartCursorAsync(
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
        var cacheKey = "QueryBatchSmartCursorAsync:" + HashParameters.Calculate(
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

        var query = () => _meta.QueryBatchSmartCursorAsync(
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

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            GetDriveIdInvalidationTags(driveId));

        return result;
    }

    //

    public async Task<(List<DriveMainIndexRecord>, bool moreRows, string cursor)> QueryModifiedAsync(
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
        var cacheKey = "QueryModifiedAsync:" + HashParameters.Calculate(
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

        var query = () => _meta.QueryModifiedAsync(
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

        var result = await Cache.GetOrSetAsync(
            cacheKey,
            _ => query(),
            cacheTtl ?? DefaultTtl,
            GetDriveIdInvalidationTags(driveId));

        return result;
    }
}
