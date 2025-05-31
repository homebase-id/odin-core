using System;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract IDriveManager DriveManager { get; }

    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    public abstract Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext);

    public abstract Task<bool> CanWriteToDrive(Guid driveId, IOdinContext odinContext);
    
    /// <summary>
    /// Enforces that the caller can read or write to a drive.  Useful basic operations such as file exists
    /// </summary>
    public abstract Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext);
}