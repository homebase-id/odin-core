using System;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Comment
{
    public class CommentFileQueryService : DriveQueryServiceBase
    {

        public CommentFileQueryService(OdinContextAccessor contextAccessor, DriveDatabaseHost driveDatabaseHost, DriveManager driveManager, CommentFileStorageService commentStorage) : 
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

        public override void AssertCanReadOrWriteToDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                var pc = ContextAccessor.GetCurrent().PermissionsContext;
                var hasPermissions = pc.HasDrivePermission(driveId, DrivePermission.Comment) ||
                                     pc.HasDrivePermission(driveId, DrivePermission.Read);

                if (!hasPermissions)
                {
                    throw new OdinSecurityException($"Unauthorized to read or write drive [{driveId}]");
                }
            }
        }
        
        protected override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Comment;
        }
    }
}