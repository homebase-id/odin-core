using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Comment
{
    public class CommentFileQueryService : DriveQueryServiceBase
    {

        public CommentFileQueryService(DotYouContextAccessor contextAccessor, DriveDatabaseHost driveDatabaseHost, DriveManager driveManager, CommentFileStorageService commentStorage) : 
            base(contextAccessor, driveDatabaseHost, driveManager, commentStorage)
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

        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Comment;
        }
    }
}