using System;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract IDriveManager DriveManager { get; }

    /// <summary>
    /// Enforces drive permissions when reading files: the read rule's drive step (<see cref="PermissionContext.AssertCanReadDrive(StorageDrive)"/>)
    /// </summary>
    public async Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await DriveManager.GetDriveAsync(driveId, true);
        odinContext.PermissionsContext.AssertCanReadDrive(drive);
    }

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext);

    public abstract Task<bool> CanWriteToDrive(Guid driveId, IOdinContext odinContext);
    
    /// <summary>
    /// The permission that, besides Read, lets a caller at a drive's files for basic operations such as file exists:
    /// Write on the standard file system, Comment on the comment one
    /// </summary>
    protected abstract DrivePermission ReadOrWritePermission { get; }

    /// <summary>
    /// Enforces that the caller can read or write to a drive.  Useful basic operations such as file exists
    /// </summary>
    public async Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await DriveManager.GetDriveAsync(driveId, true);
        odinContext.PermissionsContext.AssertCanReadOrWriteDrive(drive, ReadOrWritePermission);
    }
}