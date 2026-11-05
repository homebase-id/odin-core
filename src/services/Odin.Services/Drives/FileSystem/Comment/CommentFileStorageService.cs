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
using Odin.Services.Drives.FileSystem.Base.Ttl;
using Odin.Services.Drives.Management;
using Odin.Services.Registry.PayloadMove;

namespace Odin.Services.Drives.FileSystem.Comment;

public class CommentFileStorageService(
    ILogger<DriveStorageServiceBase> logger,
    IMediator mediator,
    IDriveAclAuthorizationService driveAclAuthorizationService,
    IDriveManager driveManager,
    LongTermStorageManager longTermStorageManager,
    UploadStorageManager uploadStorageManager,
    InboxStorageManager inboxStorageManager,
    // OrphanTestUtil orphanTestUtil,
    IdentityDatabase db,
    InboxFileStore inboxFileStore,
    UploadFileStore uploadFileStore,
    FileExpiryScheduler fileExpiryScheduler,
    PayloadMoveArrivals payloadMoveArrivals
)
    : DriveStorageServiceBase(
        logger,
        mediator,
        driveAclAuthorizationService,
        driveManager,
        longTermStorageManager,
        uploadStorageManager,
        inboxStorageManager,
        // orphanTestUtil,
        db,
        inboxFileStore,
        uploadFileStore,
        fileExpiryScheduler,
        payloadMoveArrivals)
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
        // On a Selective Share drive the share's Comment bit is asked too, of each comment where its header is written
        // (DriveStorageServiceBase.AssertMayWriteSharedCommentAsync)
        var drive = await DriveManager.GetDriveAsync(driveId, true);
        if (!PrivateDriveAllowsCaller(drive, odinContext))
        {
            throw new OdinSecurityException("Only owner callers comment on a Private drive");
        }

        if (!drive.AllowAnonymousReads)
        {
            odinContext.PermissionsContext.AssertHasDrivePermission(driveId, DrivePermission.Comment);
        }
    }

    /// <summary>Only owner callers comment on a Private drive: nobody else may read its files (docs/terminology.md).</summary>
    private static bool PrivateDriveAllowsCaller(StorageDrive drive, IOdinContext odinContext) =>
        drive.SharingModel != DriveSharingModel.Private || odinContext.Caller.IsOwner;

    public override async Task<bool> CanWriteToDrive(Guid driveId, IOdinContext odinContext)
    {
        var drive = await DriveManager.GetDriveAsync(driveId, true);

        if (!PrivateDriveAllowsCaller(drive, odinContext))
        {
            return false;
        }

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