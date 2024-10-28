using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Comment
{
    public class CommentFileQueryService : DriveQueryServiceBase
    {
        public CommentFileQueryService(
            ILogger<CommentFileQueryService> logger,
            DriveDatabaseHost driveDatabaseHost, DriveManager driveManager,
            CommentFileStorageService commentStorage) :
            base(logger, driveDatabaseHost, driveManager, commentStorage)
        {
        }

        public override async Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, db, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, db, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, db, true);
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