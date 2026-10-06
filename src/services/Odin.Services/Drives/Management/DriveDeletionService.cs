using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Drives.Management;

#nullable enable

/// <summary>
/// The owner's bulk removal of drive content. Local only: nothing is sent to peers, and copies they already
/// received stay with them.
/// </summary>
public class DriveDeletionService(
    DriveManager driveManager,
    LongTermStorageManager longTermStorageManager,
    DriveQuery driveQuery,
    CircleNetworkService circleNetworkService,
    IAppRegistrationService appRegistrationService,
    IdentityDatabase db,
    TenantContext tenantContext,
    LongTermPayloadStore longTermPayloadStore,
    UploadFileStore uploadFileStore,
    InboxFileStore inboxFileStore)
{
    /// <summary>
    /// Hard-deletes every file on a drive, of every file system type, and keeps the drive. System drives are
    /// refused: their content is what the identity runs on.
    /// </summary>
    public async Task EmptyDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await GetDeletableDriveAsync(driveId, odinContext);
        await longTermStorageManager.DeleteAllFilesAsync(drive);
    }

    /// <summary>
    /// Deletes an archived, non-system drive and everything in it: its files, its followers, and every grant
    /// that names it -- circles, connections, YouAuth domains, deposits and apps -- so a drive created later
    /// with the same alias, as an app reinstall does, inherits none of them.
    /// </summary>
    /// <remarks>
    /// The database changes are one transaction. The drive's directories go after it commits: a storage failure
    /// can leave orphaned files, but never a drive record without its data.
    /// </remarks>
    public async Task DeleteDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await GetDeletableDriveAsync(driveId, odinContext);
        if (!drive.IsArchived)
        {
            throw new OdinClientException("Archive the drive before deleting it");
        }

        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await circleNetworkService.RemoveDriveFromAllGrantsAsync(driveId, odinContext);
            await appRegistrationService.RemoveDriveFromAllAppsAsync(driveId, odinContext);
            await driveQuery.DeleteDriveContentAsync(driveId);
            await db.FollowsMeCached.DeleteByDriveAsync(driveId);
            await db.DrivesCached.DeleteAsync(driveId);
            tx.Commit();
        }

        var paths = tenantContext.TenantPathManager;
        foreach (var (store, directory) in new (IDriveFileStore, string)[]
                 {
                     (longTermPayloadStore, paths.GetDrivePath(driveId)),
                     (uploadFileStore, paths.GetDriveUploadPath(driveId)),
                     (inboxFileStore, paths.GetDriveInboxPath(driveId))
                 })
        {
            TenantPathManager.AssertIsDriveDirectory(directory, driveId);
            await store.DeleteDirectoryAsync(directory);
        }
    }

    private async Task<StorageDrive> GetDeletableDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (BuiltinDrives.IsProtected(driveId))
        {
            throw new OdinClientException("Cannot delete a system drive or its content");
        }

        return (await driveManager.GetDriveAsync(driveId, failIfInvalid: true))!;
    }
}
