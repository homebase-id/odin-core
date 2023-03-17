using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService : DriveStorageServiceBase
{
    public CommentFileStorageService(DotYouContextAccessor contextAccessor, ILoggerFactory loggerFactory, IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,DriveManager driveManager) :
        base(contextAccessor, loggerFactory, mediator, driveAclAuthorizationService,driveManager)
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
            ContextAccessor.GetCurrent().PermissionsContext.AssertCanWriteReactionsAndCommentsToDrive(driveId);
        }
    }
    
    public override FileSystemType GetFileSystemType()
    {
        return FileSystemType.Comment;
    }
}