using System;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.Management;

namespace Odin.Core.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract DriveManager DriveManager { get; }

    protected abstract OdinContextAccessor ContextAccessor { get; }
    
    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    public abstract void AssertCanReadDrive(Guid driveId);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract void AssertCanWriteToDrive(Guid driveId);

    /// <summary>
    /// Enforces that the caller can read or write to a drive.  Useful basic operations such as file exists
    /// </summary>
    public abstract void AssertCanReadOrWriteToDrive(Guid driveId);
    
}