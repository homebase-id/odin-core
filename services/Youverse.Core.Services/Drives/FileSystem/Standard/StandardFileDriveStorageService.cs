using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Standard
{
    public class StandardFileDriveStorageService : DriveStorageServiceBase
    {
        public StandardFileDriveStorageService(DotYouContextAccessor contextAccessor, ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, DriveManager driveManager) :
            base(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService, driveManager)
        {
        }

        protected override void AssertCanReadDrive(Guid driveId)
        {
            var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        protected override void AssertCanWriteToDrive(Guid driveId)
        {
            var drive = this.DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);
            }
        }

        public override FileSystemType GetFileSystemType()
        {
            return FileSystemType.Standard;
        }
    }
}