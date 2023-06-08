using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService : DriveStorageServiceBase
{
    public CommentFileStorageService(OdinContextAccessor contextAccessor, ILoggerFactory loggerFactory, IMediator mediator,
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

    public override void AssertCanReadOrWriteToDrive(Guid driveId)
    {
        var drive = DriveManager.GetDrive(driveId, true).GetAwaiter().GetResult();
        if (!drive.AllowAnonymousReads)
        {
            var pc = ContextAccessor.GetCurrent().PermissionsContext;
            var hasPermissions = pc.HasDrivePermission(driveId, DrivePermission.WriteReactionsAndComments) ||
                                 pc.HasDrivePermission(driveId, DrivePermission.Read);

            if (!hasPermissions)
            {
                throw new YouverseSecurityException($"Unauthorized to read or write drive [{driveId}]");
            }
        }
    }

    public override FileSystemType GetFileSystemType()
    {
        return FileSystemType.Comment;
    }
}