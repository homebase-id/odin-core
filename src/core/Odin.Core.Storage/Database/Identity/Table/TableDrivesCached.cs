using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableDrivesCached(TableDrives table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, RootInvalidationTag)
{
    public const string RootInvalidationTag = nameof(TableDrives);

    //

    private string GetDriveIdCacheKey(Guid driveId)
    {
        return "driveId:" + driveId;
    }

    //

    private string GetDriveTypeCacheKey(Guid driveType)
    {
        return "driveType:" + driveType;
    }

    //

    private string GetDriveListCacheKey(int count, Int64? cursor)
    {
        return "count:" + count + ":cursor:" + (cursor?.ToString() ?? "");
    }

    //

    private string GetTargetDriveCacheKey(Guid driveAlias, Guid driveType)
    {
        return "driveAlias:" + driveAlias + ":driveType:" + driveType;
    }

    //

    private string GetCountCacheKey()
    {
        return "count";
    }

    //

    public async Task<DrivesRecord?> GetAsync(Guid driveId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetDriveIdCacheKey(driveId),
            _ => table.GetAsync(driveId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<DrivesRecord>> GetDrivesByTypeAsync(Guid driveType, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetDriveTypeCacheKey(driveType),
            _ => table.GetDrivesByTypeAsync(driveType),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> InsertAsync(DrivesRecord item)
    {
        var result = await table.InsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<bool> TryInsertAsync(DrivesRecord item)
    {
        var result = await table.TryInsertAsync(item);
        if (result)
        {
            await Cache.InvalidateAllAsync();
        }
        return result;
    }

    //

    public async Task<int> UpsertAsync(DrivesRecord item)
    {
        var result = await table.UpsertAsync(item);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<(List<DrivesRecord>, UnixTimeUtc? nextCursor, long nextRowId)> GetList(int count, Int64? inCursor, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetDriveListCacheKey(count, inCursor),
            _ => table.GetList(count, inCursor),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<DrivesRecord?> GetByTargetDriveAsync(Guid driveAlias, Guid driveType, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetTargetDriveCacheKey(driveAlias, driveType),
            _ => table.GetByTargetDriveAsync(driveAlias, driveType),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> GetCountAsync(TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCountCacheKey(),
            _ => table.GetCountAsync(),
            ttl ?? DefaultTtl);
        return result;
    }

    //

}
