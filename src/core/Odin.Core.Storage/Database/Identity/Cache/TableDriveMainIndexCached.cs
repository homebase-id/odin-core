using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableDriveMainIndexCached(
    TableDriveMainIndex table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) :
    AbstractTableCaching(cache, scopedConnectionFactory, CommonDriveRootTag)
{
    public const string CommonDriveRootTag = nameof(TableDriveMainIndexCached);

    //

    private static string GetCacheKey(Guid driveId)
    {
        return driveId.ToString();
    }

    //

    private static string GetUniqueIdCacheKey(Guid driveId, Guid? uniqueId)
    {
        return driveId + ":unique:" + (uniqueId?.ToString() ?? "");
    }

    //

    private static string GetGlobalTransitIdCacheKey(Guid driveId, Guid? globalTransitId)
    {
        return driveId + ":globaltransit:" + (globalTransitId?.ToString() ?? "");
    }

    //

    private static string GetFileIdCacheKey(Guid driveId, Guid fileId)
    {
        return driveId + ":file:" + fileId;
    }

    //

    private static string GetDriveSizeCacheKey(Guid driveId)
    {
        return driveId + ":size";
    }

    //

    private static string GetTotalSizeAllDrivesCacheKey()
    {
        return "totalsizealldrives";
    }

    //

    internal static List<string> GetDriveIdTags(Guid driveId)
    {
        return ["driveId:" + driveId];
    }

    //

    private async Task InvalidateDriveAsync(Guid driveId)
    {
        await InvalidateAsync([
            CreateRemoveByTagsAction(GetDriveIdTags(driveId)),
            CreateRemoveByKeyAction(GetTotalSizeAllDrivesCacheKey()),
        ]);
    }

    //

    public async Task<List<DriveMainIndexRecord>> GetAllByDriveIdAsync(Guid driveId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetCacheKey(driveId),
            _ => table.GetAllByDriveIdAsync(driveId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetByUniqueIdAsync(Guid driveId, Guid? uniqueId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetUniqueIdCacheKey(driveId, uniqueId),
            _ => table.GetByUniqueIdAsync(driveId, uniqueId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetByGlobalTransitIdAsync(Guid driveId, Guid? globalTransitId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetGlobalTransitIdCacheKey(driveId, globalTransitId),
            _ => table.GetByGlobalTransitIdAsync(driveId, globalTransitId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<DriveMainIndexRecord?> GetAsync(Guid driveId, Guid fileId, TimeSpan ttl)
    {
        // CanUpdateLocalAppMetadataTagsWhenNotSetInTargetFile
        var result = await GetOrSetAsync(
            GetFileIdCacheKey(driveId, fileId),
            _ => table.GetAsync(driveId, fileId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<int> InsertAsync(DriveMainIndexRecord item)
    {
        var result = await table.InsertAsync(item);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    public async Task<int> DeleteAsync(Guid driveId, Guid fileId)
    {
        var result = await table.DeleteAsync(driveId, fileId);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<int> UpsertAllButReactionsAndTransferAsync(
        DriveMainIndexRecord item,
        Guid? useThisNewVersionTag = null)
    {
        var result = await table.UpsertAllButReactionsAndTransferAsync(item, useThisNewVersionTag);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    public async Task<int> UpdateReactionSummaryAsync(Guid driveId, Guid fileId, string reactionSummary)
    {
        var result = await table.UpdateReactionSummaryAsync(driveId, fileId, reactionSummary);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<(int, long)> UpdateTransferSummaryAsync(Guid driveId, Guid fileId, string transferHistory)
    {
        var result = await table.UpdateTransferSummaryAsync(driveId, fileId, transferHistory);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

    public async Task<(Int64, Int64)> GetDriveSizeAsync(Guid driveId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetDriveSizeCacheKey(driveId),
            _ => table.GetDriveSizeAsync(driveId),
            ttl,
            GetDriveIdTags(driveId));
        return result;
    }

    //

    public async Task<long> GetTotalSizeAllDrivesAsync(TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetTotalSizeAllDrivesCacheKey(),
            _ => table.GetTotalSizeAllDrivesAsync(),
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
        var result = await table.UpdateLocalAppMetadataAsync(driveId, fileId, oldVersionTag, newVersionTag, localMetadataJson);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    // For defragmenter only
    public async Task<int> RawUpdateAsync(DriveMainIndexRecord item)
    {
        var result = await table.RawUpdateAsync(item);
        await InvalidateDriveAsync(item.driveId);
        return result;
    }

    //

    // For defragmenter only. Updates the byteCount column in the DB.
    public async Task<int> UpdateByteCountAsync(Guid driveId, Guid fileId, long byteCount)
    {
        var result = await table.UpdateByteCountAsync(driveId, fileId, byteCount);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    //

}
