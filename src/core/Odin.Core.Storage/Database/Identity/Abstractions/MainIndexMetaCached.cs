using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

#nullable enable

public class MainIndexMetaCached : AbstractTableCaching
{
    private readonly MainIndexMeta _meta;
    private readonly TableDriveMainIndexCacheKeys _cacheKeys;

    public MainIndexMetaCached(MainIndexMeta meta, IIdentityTransactionalCacheFactory cacheFactory)
        : base(cacheFactory, meta.GetType().Name, TableDriveMainIndexCacheKeys.RootInvalidationTag)
    {
        _meta = meta;
        _cacheKeys = new TableDriveMainIndexCacheKeys(Cache);
    }

    //

    private Task InvalidateDriveAsync(Guid driveId)
    {
        return _cacheKeys.InvalidateDriveAsync(driveId);
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
}
