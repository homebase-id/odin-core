using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;

namespace Youverse.Core.Services.Drives.FileSystem.Standard;

public class StandardDriveCommandService : DriveCommandServiceBase
{
    public StandardDriveCommandService(
        DriveDatabaseHost driveDatabaseHost, 
        StandardFileDriveStorageService storage, 
        DotYouContextAccessor contextAccessor, 
        DriveManager driveManager) : 
        base(driveDatabaseHost, storage, contextAccessor, driveManager)
    {
    }

    public override void AssertCanReadDrive(Guid driveId)
    {
        var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
        if (!drive.AllowAnonymousReads)
        {
            ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
        }
    }

    public override void AssertCanWriteToDrive(Guid driveId)
    {
        var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
        if (!drive.AllowAnonymousReads)
        {
            ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
        }
    }
}