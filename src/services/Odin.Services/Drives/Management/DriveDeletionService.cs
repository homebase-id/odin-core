using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.Management;

#nullable enable

/// <summary>
/// The owner's bulk removal of drive content (#1869). Local only: nothing is sent to peers, and copies
/// they already received stay with them.
/// </summary>
public class DriveDeletionService(DriveManager driveManager, LongTermStorageManager longTermStorageManager)
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

    private async Task<StorageDrive> GetDeletableDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var drive = await driveManager.GetDriveAsync(driveId, failIfInvalid: true);
        if (BuiltinDrives.IsProtected(drive!.Id))
        {
            throw new OdinClientException("Cannot delete the content of a system drive");
        }

        return drive;
    }
}
