using System;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class RequirePermissionsBase
{
    protected abstract DriveManager DriveManager { get; }

    /// <summary>
    /// Enforces drive permissions when reading files
    /// </summary>
    public abstract Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext, IdentityDatabase db);

    /// <summary>
    /// Enforces drive permissions when writing files
    /// </summary>
    public abstract Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db);

    /// <summary>
    /// Enforces that the caller can read or write to a drive.  Useful basic operations such as file exists
    /// </summary>
    public abstract Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext, IdentityDatabase db);
}