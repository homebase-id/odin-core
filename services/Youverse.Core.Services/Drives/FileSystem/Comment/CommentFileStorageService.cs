using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Base;

namespace Youverse.Core.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService : DriveStorageServiceBase
{
    public CommentFileStorageService(DotYouContextAccessor contextAccessor, ILoggerFactory loggerFactory, IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,DriveManager driveManager) :
        base(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService,driveManager)
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

    protected override FileSystemType GetFileSystemType()
    {
        return FileSystemType.Comment;
    }
}