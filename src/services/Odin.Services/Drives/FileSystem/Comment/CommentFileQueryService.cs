using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Comment
{
    public class CommentFileQueryService : DriveQueryServiceBase
    {
        public CommentFileQueryService(DriveDatabaseHost driveDatabaseHost, DriveManager driveManager,
            CommentFileStorageService commentStorage) :
            base(driveDatabaseHost, driveManager, commentStorage)
        {
        }

        public override async Task AssertCanReadDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                var pc = odinContext.PermissionsContext;
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