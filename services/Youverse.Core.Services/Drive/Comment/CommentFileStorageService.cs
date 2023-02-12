using System;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drive.Comment;

public class CommentFileStorageService : DriveServiceBase
{
    public CommentFileStorageService(DotYouContextAccessor contextAccessor, ITenantSystemStorage tenantSystemStorage, ILoggerFactory loggerFactory, IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService, TenantContext tenantContext,DriveManager driveManager) :
        base(contextAccessor, tenantSystemStorage, loggerFactory, mediator, driveAclAuthorizationService, tenantContext,driveManager)
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
}