using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Drives.Base;

public abstract class RequirePermissionsBase
{
    protected abstract DriveManager DriveManager { get; }

    protected abstract DotYouContextAccessor ContextAccessor { get; }
    
    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    protected abstract void AssertCanReadDrive(Guid driveId);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    protected abstract void AssertCanWriteToDrive(Guid driveId);

}