using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveQueryService : DriveQueryServiceBase
    {
        public StandardFileDriveQueryService(
            
            DriveDatabaseHost driveDatabaseHost,
            DriveManager driveManager,
            StandardFileDriveStorageService storage) :
            base( driveDatabaseHost, driveManager, storage)
        {
        }

        public override async Task AssertCanReadDrive(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDrive(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
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

        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}