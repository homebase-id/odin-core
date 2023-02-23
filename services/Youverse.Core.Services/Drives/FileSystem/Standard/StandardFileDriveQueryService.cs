using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Base;
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

        protected override void AssertCanReadDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        protected override void AssertCanWriteToDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}