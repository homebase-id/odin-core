using System;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Base;

namespace Youverse.Core.Services.Drives.FileSystem.Comment
{
    public class CommentFileQueryService : DriveQueryServiceBase
    {

        public CommentFileQueryService(DotYouContextAccessor contextAccessor, DriveDatabaseHost driveDatabaseHost, DriveManager driveManager, CommentFileStorageService commentStorage) : 
            base(contextAccessor, driveDatabaseHost, driveManager, commentStorage)
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
    }
}