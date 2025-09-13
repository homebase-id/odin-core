using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

internal class TableDriveMainIndexCacheKeys(TransactionalCache cache)
{
    internal const string RootInvalidationTag = "DriveMainIndex";

    //

    internal string GetCacheKey(Guid driveId)
    {
        return driveId.ToString();
    }

    //

    internal string GetUniqueIdCacheKey(Guid driveId, Guid? uniqueId)
    {
        return driveId + ":unique:" + (uniqueId?.ToString() ?? "");
    }

    //

    internal string GetGlobalTransitIdCacheKey(Guid driveId, Guid? globalTransitId)
    {
        return driveId + ":globaltransit:" + (globalTransitId?.ToString() ?? "");
    }

    //

    internal string GetFileIdCacheKey(Guid driveId, Guid fileId)
    {
        return driveId + ":file:" + fileId;
    }

    //

    internal string GetDriveSizeCacheKey(Guid driveId)
    {
        return driveId + ":size";
    }

    //

    internal string GetTotalSizeAllDrivesCacheKey()
    {
        return "totalsizealldrives";
    }

    //

    internal List<string> GetDriveIdTags(Guid driveId)
    {
        return ["driveId:" + driveId];
    }

    //

    internal async Task InvalidateDriveAsync(Guid driveId)
    {
        await cache.InvalidateAsync([
            cache.CreateRemoveByTagsAction(GetDriveIdTags(driveId)),
            cache.CreateRemoveByKeyAction(GetTotalSizeAllDrivesCacheKey()),
        ]);
    }

    //

}