using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveQueryService : DriveQueryServiceBase
    {
        public StandardFileDriveQueryService(
            ILogger<StandardFileDriveQueryService> logger,
            DriveDatabaseHost driveDatabaseHost,
            DriveManager driveManager,
            StandardFileDriveStorageService storage) :
            base(logger, driveDatabaseHost, driveManager, storage)
        {
        }

        public override async Task AssertCanReadDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDrive(driveId, db, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDrive(driveId, db, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDrive(driveId, db, true);
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