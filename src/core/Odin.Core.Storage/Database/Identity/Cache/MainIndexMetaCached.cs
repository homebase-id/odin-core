using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Cache.Helpers;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class MainIndexMetaCached : AbstractTableCaching
{
    private readonly MainIndexMeta _meta;
    private readonly DriveMainIndexCacheHelper _cacheHelper;

    public MainIndexMetaCached(MainIndexMeta meta, IIdentityTransactionalCacheFactory cacheFactory)
        : base(cacheFactory, meta.GetType().Name, DriveMainIndexCacheHelper.RootTag)
    {
        _meta = meta;
        _cacheHelper = new DriveMainIndexCacheHelper(Cache);
    }

    //

    private Task InvalidateDriveAsync(Guid driveId)
    {
        return _cacheHelper.InvalidateDriveAsync(driveId);
    }

    //

    public async Task<int> DeleteEntryAsync(Guid driveId, Guid fileId)
    {
        var result = await _meta.DeleteEntryAsync(driveId, fileId);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
    {
        await _meta.UpdateLocalTagsAsync(driveId, fileId, tags);
        await InvalidateDriveAsync(driveId);
    }

    //

    public async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
        List<Guid>? accessControlList = null,
        List<Guid>? tagIdList = null,
        Guid? useThisNewVersionTag = null)
    {
        var result = await _meta.BaseUpsertEntryZapZapAsync(
            driveMainIndexRecord,
            accessControlList,
            tagIdList,
            useThisNewVersionTag);

        await InvalidateDriveAsync(driveMainIndexRecord.driveId);

        return result;
    }

    //

    public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchAsync(
        Guid driveId,
        int noOfItems,
        QueryBatchCursor? cursor,
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
        List<Guid>? localTagsAllOf = null)
    {
        // SEB:TODO
        var result = await _meta.QueryBatchAsync(
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

        return result;
    }

    //

    public async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> QueryBatchSmartCursorAsync(
        Guid driveId,
        int noOfItems,
        QueryBatchCursor? cursor,
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
        List<Guid>? localTagsAllOf = null)
    {
        // SEB:TODO
        var result = await _meta.QueryBatchSmartCursorAsync(
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

        return result;

    }

    //

    public async Task<(List<DriveMainIndexRecord>, bool moreRows, string cursor)> QueryModifiedAsync(Guid driveId,
        int noOfItems,
        string cursorString,
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
        List<Guid>? localTagsAllOf = null)
    {
        // SEB:TODO
        var result = await _meta.QueryModifiedAsync(
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

        return result;
    }
}
