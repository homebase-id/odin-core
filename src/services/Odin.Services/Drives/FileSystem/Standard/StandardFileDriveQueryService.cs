using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveQueryService(
        IOdinContextAccessor contextAccessor,
        DriveDatabaseHost driveDatabaseHost,
        DriveManager driveManager,
        StandardFileDriveStorageService storage)
        : DriveQueryServiceBase(contextAccessor, driveDatabaseHost, driveManager, storage)
    {
        public override async Task AssertCanReadDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
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

        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}