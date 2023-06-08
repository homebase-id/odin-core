using System;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;

namespace Odin.Core.Services.Drives.FileSystem.Standard;

public class StandardDriveCommandService : DriveCommandServiceBase
{
    public StandardDriveCommandService(
        DriveDatabaseHost driveDatabaseHost, 
        StandardFileDriveStorageService storage, 
        OdinContextAccessor contextAccessor, 
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
    
    public override void AssertCanReadOrWriteToDrive(Guid driveId)
    {
        var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
        if (!drive.AllowAnonymousReads)
        {
            var pc = ContextAccessor.GetCurrent().PermissionsContext;
            var hasPermissions = pc.HasDrivePermission(driveId, DrivePermission.Write) ||
                                 pc.HasDrivePermission(driveId, DrivePermission.Read);

            if (!hasPermissions)
            {
                throw new OdinSecurityException($"Unauthorized to read or write drive [{driveId}]");
            }
        }
    }
}