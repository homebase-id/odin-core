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

    public async Task<int> DeleteEntryAsync(Guid driveId, Guid fileId, Guid? expectedVersionTag = null)
    {
        var result = await _meta.DeleteEntryAsync(driveId, fileId, expectedVersionTag);

        // Also when nothing was deleted: the caller may have acted on a stale cached header, which this evicts
        await InvalidateDriveAsync(driveId);
        return result;
    }

    public Task<List<Guid>> GetDriveFileIdsAsync(Guid driveId, int limit, long? createdAtOrBefore) =>
        _meta.GetDriveFileIdsAsync(driveId, limit, createdAtOrBefore);

    public Task<long> CountDriveFilesAsync(Guid driveId, long? createdAtOrBefore) =>
        _meta.CountDriveFilesAsync(driveId, createdAtOrBefore);

    public async Task<long> DeleteFilesAsync(Guid driveId, IReadOnlyList<Guid> fileIds)
    {
        var result = await _meta.DeleteFilesAsync(driveId, fileIds);
        await InvalidateDriveAsync(driveId);
        return result;
    }

    public async Task<long> DeleteDriveContentAsync(Guid driveId)
    {
        var result = await _meta.DeleteDriveContentAsync(driveId);
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

    /// <inheritdoc cref="MainIndexMeta.GetLockedRequiredSecurityGroupAsync"/>
    public Task<int?> GetLockedRequiredSecurityGroupAsync(Guid driveId, Guid fileId) =>
        _meta.GetLockedRequiredSecurityGroupAsync(driveId, fileId);

    /// <summary>
    /// Writes each file's shares (<see cref="MainIndexMeta.WriteSharesAsync"/>), then clears the drive's cache once.
    /// Returns the first file that doesn't exist, writing nothing after it; null when all were written.
    /// </summary>
    public async Task<Guid?> WriteSharesAsync(Guid driveId, IEnumerable<FileShares> files)
    {
        try
        {
            foreach (var file in files)
            {
                if (!await _meta.WriteSharesAsync(driveId, file.FileId, file.Rows, file.CheckFile))
                {
                    return file.FileId;
                }
            }

            return null;
        }
        finally
        {
            await InvalidateDriveAsync(driveId);
        }
    }

    public async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
        List<Guid>? accessControlList = null,
        List<Guid>? tagIdList = null,
        Guid? useThisNewVersionTag = null,
        bool reconcileAcl = true)
    {
        var result = await _meta.BaseUpsertEntryZapZapAsync(
            driveMainIndexRecord,
            accessControlList,
            tagIdList,
            useThisNewVersionTag,
            reconcileAcl);

        await InvalidateDriveAsync(driveMainIndexRecord.driveId);

        return result;
    }
}

/// <summary>
/// A file's share rows to write, with the check of its row, read locked, that may refuse it or lift its group
/// (<see cref="MainIndexMeta.WriteSharesAsync"/>).
/// </summary>
public sealed record FileShares(
    Guid FileId,
    IReadOnlyCollection<DriveAclIndexRecord> Rows,
    Func<DriveMainIndexRecord, (int requiredSecurityGroup, string hdrServerData)?> CheckFile);
