using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveStorageService(
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        DriveManager driveManager,
        LongTermStorageManager longTermStorageManager,
        UploadStorageManager uploadStorageManager,
        OrphanTestUtil orphanTestUtil,
        IdentityDatabase db)
        : DriveStorageServiceBase(loggerFactory, mediator, driveAclAuthorizationService, driveManager, longTermStorageManager,
            uploadStorageManager, orphanTestUtil, db)
    {
        public override async Task AssertCanReadDriveAsync(Guid driveId, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(driveId, true);
            if (!drive.AllowAnonymousReads)
            {
                odinContext.PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            return Task.CompletedTask;
        }

        public override Task<bool> CanWriteToDrive(Guid driveId, IOdinContext odinContext)
        {
            return Task.FromResult(odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Write));
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

        public override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}