using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveStorageService : DriveStorageServiceBase
    {
        public StandardFileDriveStorageService(OdinContextAccessor contextAccessor, ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, DriveManager driveManager, OdinConfiguration odinConfiguration, DriveFileReaderWriter driveFileReaderWriter) :
            base(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager, odinConfiguration, driveFileReaderWriter)
        {
        }

        public override void AssertCanReadDrive(Guid driveId)
        {
            var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public override void AssertCanWriteToDrive(Guid driveId)
        {
            // var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            // if (!drive.AllowAnonymousReads)
            // {
            //     ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
            // }

            ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
        }

        public override void AssertCanReadOrWriteToDrive(Guid driveId)
        {
            var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                var pc = ContextAccessor.GetCurrent().PermissionsContext;
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