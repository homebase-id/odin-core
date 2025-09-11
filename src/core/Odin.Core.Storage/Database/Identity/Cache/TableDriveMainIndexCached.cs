using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Cache.Helpers;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableDriveMainIndexCached : AbstractTableCaching
{
    private readonly TableDriveMainIndex _table;
    private readonly DriveMainIndexCacheHelper _cacheHelper;

    public TableDriveMainIndexCached(TableDriveMainIndex table, IIdentityTransactionalCacheFactory cacheFactory) :
        base(cacheFactory, DriveMainIndexCacheHelper.RootTag)
    {
        _table = table;
        _cacheHelper = new DriveMainIndexCacheHelper(Cache);
    }

    //

    private string GetCacheKey(Guid driveId)
    {
        return _cacheHelper.GetCacheKey(driveId);
    }

    //

    private string GetUniqueIdCacheKey(Guid driveId, Guid? uniqueId)
    {
        return _cacheHelper.GetUniqueIdCacheKey(driveId, uniqueId);
    }

    //

    private string GetGlobalTransitIdCacheKey(Guid driveId, Guid? globalTransitId)
    {
        return _cacheHelper.GetGlobalTransitIdCacheKey(driveId, globalTransitId);
    }

    //

    private string GetFileIdCacheKey(Guid driveId, Guid fileId)
    {
        return _cacheHelper.GetFileIdCacheKey(driveId, fileId);
    }

    //

    private string GetDriveSizeCacheKey(Guid driveId)
    {
        return _cacheHelper.GetDriveSizeCacheKey(driveId);
    }

    //

    private string GetTotalSizeAllDrivesCacheKey()
    {
        return _cacheHelper.GetTotalSizeAllDrivesCacheKey();
    }

    //

    private List<string> GetDriveIdTags(Guid driveId)
    {
        return _cacheHelper.GetDriveIdTags(driveId);
    }

    //

    private Task InvalidateDriveAsync(Guid driveId)
    {
        return _cacheHelper.InvalidateDriveAsync(driveId);
    }

    //

    public async Task<List<DriveMainIndexRecord>> GetAllByDriveIdAsync(Guid driveId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(driveId),
            _ => _table.GetAllByDriveIdAsync(driveId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetByUniqueIdAsync(Guid driveId, Guid? uniqueId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetUniqueIdCacheKey(driveId, uniqueId),
            _ => _table.GetByUniqueIdAsync(driveId, uniqueId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetByGlobalTransitIdAsync(Guid driveId, Guid? globalTransitId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetGlobalTransitIdCacheKey(driveId, globalTransitId),
            _ => _table.GetByGlobalTransitIdAsync(driveId, globalTransitId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetAsync(Guid driveId, Guid fileId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetFileIdCacheKey(driveId, fileId),
            _ => _table.GetAsync(driveId, fileId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<int> InsertAsync(DriveMainIndexRecord item)
    {
        var result = await _table.InsertAsync(item);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    public async Task<int> DeleteAsync(Guid driveId, Guid fileId)
    {
        var result = await _table.DeleteAsync(driveId, fileId);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<int> UpsertAllButReactionsAndTransferAsync(
        DriveMainIndexRecord item,
        Guid? useThisNewVersionTag = null)
    {
        var result = await _table.UpsertAllButReactionsAndTransferAsync(item, useThisNewVersionTag);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    public async Task<int> UpdateReactionSummaryAsync(Guid driveId, Guid fileId, string reactionSummary)
    {
        var result = await _table.UpdateReactionSummaryAsync(driveId, fileId, reactionSummary);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<(int, long)> UpdateTransferSummaryAsync(Guid driveId, Guid fileId, string transferHistory)
    {
        var result = await _table.UpdateTransferSummaryAsync(driveId, fileId, transferHistory);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<(Int64, Int64)> GetDriveSizeAsync(Guid driveId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetDriveSizeCacheKey(driveId),
            _ => _table.GetDriveSizeAsync(driveId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<long> GetTotalSizeAllDrivesAsync(TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            GetTotalSizeAllDrivesCacheKey(),
            _ => _table.GetTotalSizeAllDrivesAsync(),
            ttl);
        return result;
    }

    //

    public async Task<bool> UpdateLocalAppMetadataAsync(
        Guid driveId,
        Guid fileId,
        Guid oldVersionTag,
        Guid newVersionTag,
        string localMetadataJson)
    {
        var result = await _table.UpdateLocalAppMetadataAsync(driveId, fileId, oldVersionTag, newVersionTag, localMetadataJson);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    // For defragmenter only
    public async Task<int> RawUpdateAsync(DriveMainIndexRecord item)
    {
        var result = await _table.RawUpdateAsync(item);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    // For defragmenter only. Updates the byteCount column in the DB.
    public async Task<int> UpdateByteCountAsync(Guid driveId, Guid fileId, long byteCount)
    {
        var result = await _table.UpdateByteCountAsync(driveId, fileId, byteCount);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

}

