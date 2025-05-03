using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Concurrency;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService(
    ILoggerFactory loggerFactory,
    IMediator mediator,
    IDriveAclAuthorizationService driveAclAuthorizationService,
    DriveManager driveManager,
    LongTermStorageManager longTermStorageManager,
    UploadStorageManager uploadStorageManager,
    IdentityDatabase db)
    : DriveStorageServiceBase(loggerFactory, mediator, driveAclAuthorizationService, driveManager, longTermStorageManager,
        uploadStorageManager, db)
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
            odinContext.PermissionsContext.AssertHasDrivePermission(driveId, DrivePermission.Comment);
        }
    }
    
    public override async Task<bool> CanWriteToDrive(Guid driveId, IOdinContext odinContext)
    {
        var drive = await DriveManager.GetDriveAsync(driveId, true);
        if (!drive.AllowAnonymousReads)
        {
            return odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Write);
        }

        return true;
    }

    public override async Task AssertCanReadOrWriteToDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await DriveManager.GetDriveAsync(driveId, true);
        if (!drive.AllowAnonymousReads)
        {
            var pc = odinContext.PermissionsContext;
            var hasPermissions = pc.HasDrivePermission(driveId, DrivePermission.Comment) ||
                                 pc.HasDrivePermission(driveId, DrivePermission.Read);

            if (!hasPermissions)
            {
                throw new OdinSecurityException($"Unauthorized to read or write drive [{driveId}]");
            }
        }
    }

    public override FileSystemType GetFileSystemType()
    {
        return FileSystemType.Comment;
    }
}