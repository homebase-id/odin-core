using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService : DriveStorageServiceBase
{
    public CommentFileStorageService(ILoggerFactory loggerFactory, IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService, DriveManager driveManager, ConcurrentFileManager concurrentFileManager,
        DriveFileReaderWriter driveFileReaderWriter, DriveDatabaseHost driveDatabaseHost) :
        base(loggerFactory, mediator, driveAclAuthorizationService, driveManager, concurrentFileManager, driveFileReaderWriter, driveDatabaseHost)
    {
    }

    public override async Task AssertCanReadDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
    {
        var drive = await DriveManager.GetDrive(driveId, db, true);
        if (!drive.AllowAnonymousReads)
        {
            odinContext.PermissionsContext.AssertCanReadDrive(driveId);
        }
    }

    public override async Task AssertCanWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
    {
        var drive = await DriveManager.GetDrive(driveId, db, true);
        if (!drive.AllowAnonymousReads)
        {
            odinContext.PermissionsContext.AssertHasDrivePermission(driveId, DrivePermission.Comment);
        }
    }

    public override async Task AssertCanReadOrWriteToDrive(Guid driveId, IOdinContext odinContext, IdentityDatabase db)
    {
        var drive = await DriveManager.GetDrive(driveId, db, true);
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