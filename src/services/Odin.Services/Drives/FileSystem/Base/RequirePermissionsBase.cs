using System;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract DriveManager DriveManager { get; }

    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    public abstract Task AssertCanReadDrive(Guid driveId, OdinContext odinContext);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract Task AssertCanWriteToDrive(Guid driveId, OdinContext odinContext);

    /// <summary>
    /// Enforces that the caller can read or write to a drive.  Useful basic operations such as file exists
    /// </summary>
    public abstract Task AssertCanReadOrWriteToDrive(Guid driveId, OdinContext odinContext);
}