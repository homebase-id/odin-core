using System;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveQueryService : DriveQueryServiceBase
    {
        public StandardFileDriveQueryService(
            DotYouContextAccessor contextAccessor, 
            DriveDatabaseHost driveDatabaseHost, 
            DriveManager driveManager, 
            StandardFileDriveStorageService storage) : 
            base(contextAccessor, driveDatabaseHost, driveManager, storage)
        {
        }

        public override void AssertCanReadDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override void AssertCanWriteToDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
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
                    throw new YouverseSecurityException($"Unauthorized to read or write drive [{driveId}]");
                }
            }
        }
        
        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}