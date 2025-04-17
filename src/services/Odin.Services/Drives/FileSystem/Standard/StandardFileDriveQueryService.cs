using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveQueryService(
        ILogger<StandardFileDriveQueryService> logger,
        DriveManager driveManager,
        DriveQuery driveQuery,
        StandardFileDriveStorageService storage,
        IdentityDatabase db)
        : DriveQueryServiceBase(logger, driveManager, driveQuery, storage, db)
    {
        public override async Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override async Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, true);
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