using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.Management;

namespace Youverse.Core.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract DriveManager DriveManager { get; }

    protected abstract DotYouContextAccessor ContextAccessor { get; }
    
    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    public abstract void AssertCanReadDrive(Guid driveId);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract void AssertCanWriteToDrive(Guid driveId);

}