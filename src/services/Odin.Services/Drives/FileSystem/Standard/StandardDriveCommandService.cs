using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard;

public class StandardDriveCommandService : DriveCommandServiceBase
{
    public StandardDriveCommandService(
        DriveDatabaseHost driveDatabaseHost,
        StandardFileDriveStorageService storage,
        
        DriveManager driveManager) :
        base(driveDatabaseHost, storage,  driveManager)
    {
    }

    public override async Task AssertCanReadDrive(Guid driveId, IOdinContext odinContext, DatabaseConnection cn)
    {
        var drive = await DriveManager.GetDrive(driveId, cn, true);
        if (!drive.AllowAnonymousReads)
        {
            odinContext.PermissionsContext.AssertCanReadDrive(driveId);
        }
    }

    public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext, DatabaseConnection cn)
    {
        var drive = await DriveManager.GetDrive(driveId, cn, true);
        if (!drive.AllowAnonymousReads)
        {
            odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
        }
    }

    public override async Task AssertCanReadOrWriteToDrive(Guid driveId, IOdinContext odinContext, DatabaseConnection cn)
    {
        var drive = await DriveManager.GetDrive(driveId, cn, true);
        if (!drive.AllowAnonymousReads)
        {
            var pc = odinContext.PermissionsContext;
            var hasPermissions = pc.HasDrivePermission(driveId, DrivePermission.Write) ||
                                 pc.HasDrivePermission(driveId, DrivePermission.Read);

            if (!hasPermissions)
            {
                throw new OdinSecurityException($"Unauthorized to read or write drive [{driveId}]");
            }
        }
    }
}