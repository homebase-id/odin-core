using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Authorization.Acl;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveStorageService : DriveStorageServiceBase
    {
        public StandardFileDriveStorageService( ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, DriveManager driveManager, OdinConfiguration odinConfiguration,
            DriveFileReaderWriter driveFileReaderWriter) :
            base( loggerFactory, mediator, driveAclAuthorizationService, driveManager, odinConfiguration, driveFileReaderWriter)
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

        public override Task AssertCanWriteToDrive(Guid driveId)
        {
            odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            return Task.CompletedTask;
        }

        public override async Task AssertCanReadOrWriteToDrive(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, true);
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