#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Drives.Shares;

/// <summary>
/// A file's shares on a Selective Share drive: each is the file's <c>DriveAclIndex</c> row for a circle, with what its
/// members may do, an expiry and the file key locked with the circle's key, which the server never opens
/// (docs/selective-share-pr3-plan.md, section 3).  Written by the owner console or by the owner's app that owns both the
/// drive and the circle.
/// </summary>
public class DriveShareService(IdentityDatabase db, IDriveManager driveManager, CircleMembershipService circleMembership)
{
    public const int MaxSharesPerRequest = 100;

    /// <summary>AES-GCM under the circle key: format (1) ‖ nonce (12) ‖ file key (16) ‖ tag (16) (decision 7).</summary>
    public const int LockedFileKeyLength = 45;

    /// <summary>The bits a share may hold: React and Comment are granted by the row alone (section 10).</summary>
    public const DrivePermission ShareBits = DrivePermission.Read | DrivePermission.Write | DrivePermission.React | DrivePermission.Comment;

    public async Task<List<ShareRow>> GetSharesAsync(Guid driveId, Guid fileId, IOdinContext odinContext)
    {
        var drive = await GetDriveCallerMayShareOnAsync(driveId, odinContext);
        odinContext.PermissionsContext.AssertCanReadDrive(drive.Id);

        var rows = await db.DriveAclIndex.GetFileRowsAsync(drive.Id, fileId);
        return rows.Select(ShareRow.From).ToList();
    }

    /// <summary>
    /// Writes the shares, all or none: each is checked before anything is written, the files in one transaction.
    /// A file shared while <c>Owner</c> is lifted to <c>Connected</c> (decision A); every file's <c>modified</c>
    /// moves and its version tag stays (decision 6).
    /// </summary>
    public async Task WriteSharesAsync(Guid driveId, List<ShareRequest>? shares, IOdinContext odinContext)
    {
        var drive = await AssertMayWriteAsync(driveId, shares, odinContext);

        await using var tx = await db.BeginStackedTransactionAsync();
        foreach (var file in shares!.GroupBy(s => s.FileId))
        {
            var written = await db.MainIndexMetaCached.WriteSharesAsync(drive.Id, file.Key, ToRows(file),
                record => CheckFileAndLift(record, file.ToList()));
            if (written == null)
            {
                throw new OdinClientException($"No file {file.Key} on the drive", OdinClientErrorCode.InvalidFile);
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// A new file's upload carrying its shares (section 4): the same checks as <see cref="WriteSharesAsync"/>, before
    /// anything is stored.  The shares name no file; they are the upload's.
    /// </summary>
    public async Task AssertUploadMayShareAsync(Guid driveId, List<ShareRequest> shares, bool encrypted, IOdinContext odinContext)
    {
        if (shares.Any(s => s.FileId != Guid.Empty))
        {
            throw new OdinClientException("An upload's shares are for the file uploaded: they name no file", OdinClientErrorCode.ArgumentError);
        }

        await AssertMayWriteAsync(driveId, shares, odinContext);
        AssertKeysFit(encrypted, shares, "The file");
    }

    /// <summary>The upload's shares, written in its header's transaction, so a file is never seen half shared.</summary>
    public WriteSecondDatabaseRowBase UploadSharesWriter(InternalDriveFileId file, List<ShareRequest> shares) =>
        new WriteUploadShares(db, file, ToRows(shares));

    private sealed class WriteUploadShares(IdentityDatabase db, InternalDriveFileId file, List<DriveAclIndexRecord> rows)
        : WriteSecondDatabaseRowBase
    {
        public override async Task<int> ExecuteAsync() =>
            await db.MainIndexMetaCached.WriteSharesAsync(file.DriveId, file.FileId, rows, _ => null) == null ? 0 : 1;
    }

    /// <summary>
    /// Withdraws a share: its row stays with no bits and no locked file key, so a member's sync sees it go, as a
    /// tombstone is a deleted file's (decision E); the file's <c>modified</c> moves.  The file's group stays.
    /// </summary>
    public async Task WithdrawShareAsync(Guid driveId, Guid fileId, Guid circleId, IOdinContext odinContext)
    {
        var drive = await GetDriveCallerMayShareOnAsync(driveId, odinContext);
        AssertCallerMayWrite(drive, odinContext);
        await GetCircleCallerMayShareWithAsync(drive, circleId, odinContext);

        var row = (await db.DriveAclIndex.GetFileRowsAsync(drive.Id, fileId)).FirstOrDefault(r => r.aclMemberId == circleId);
        if (row == null)
        {
            throw new OdinClientException($"File {fileId} isn't shared with circle {circleId}", OdinClientErrorCode.ArgumentError);
        }

        if (row.permissions == 0)
        {
            return;
        }

        await db.MainIndexMetaCached.WriteSharesAsync(drive.Id, fileId,
            [new DriveAclIndexRecord { aclMemberId = circleId, permissions = 0 }], _ => null);
    }

    /// <summary>The drive, once the caller may write every one of <paramref name="shares"/> on it.</summary>
    private async Task<StorageDrive> AssertMayWriteAsync(Guid driveId, List<ShareRequest>? shares, IOdinContext odinContext)
    {
        if (shares == null || shares.Count == 0 || shares.Count > MaxSharesPerRequest)
        {
            throw new OdinClientException($"Between 1 and {MaxSharesPerRequest} shares per request", OdinClientErrorCode.ArgumentError);
        }

        if (shares.DistinctBy(s => (s.FileId, s.CircleId)).Count() != shares.Count)
        {
            throw new OdinClientException("A file is shared with a circle once per request", OdinClientErrorCode.ArgumentError);
        }

        var drive = await GetDriveCallerMayShareOnAsync(driveId, odinContext);
        AssertCallerMayWrite(drive, odinContext);
        foreach (var circleId in shares.Select(s => s.CircleId).Distinct())
        {
            await AssertCircleMayShareAsync(drive, circleId, odinContext);
        }

        var now = UnixTimeUtc.Now();
        foreach (var share in shares)
        {
            AssertShape(share, now);
        }

        return drive;
    }

    private static List<DriveAclIndexRecord> ToRows(IEnumerable<ShareRequest> shares) => shares.Select(s => new DriveAclIndexRecord
    {
        aclMemberId = s.CircleId, permissions = (int)s.Permissions, expiresAt = s.ExpiresAt, lockedFileKey = s.LockedFileKey
    }).ToList();

    /// <summary>
    /// The drive, when it is Selective Share and the caller is the owner console or the owner's app that owns it.
    /// </summary>
    private async Task<StorageDrive> GetDriveCallerMayShareOnAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(driveId, failIfInvalid: true);
        if (!drive!.IsSelectiveShare)
        {
            throw new OdinClientException($"Drive {driveId} isn't a SelectiveShare drive", OdinClientErrorCode.ArgumentError);
        }

        var caller = odinContext.Caller;
        if (caller.HasMasterKey)
        {
            return drive;
        }

        caller.AssertCallerIsOwner();
        if (caller.OdinClientContext?.AppId?.Value is not { } appId || appId != drive.AppId)
        {
            throw new OdinSecurityException($"Only the app that owns drive {driveId} may share its files");
        }

        return drive;
    }

    // A write's usual drive checks (docs/file-access.md, "Writing"): drive Write, and an archived drive only for the owner
    private static void AssertCallerMayWrite(StorageDrive drive, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertCanWriteToDrive(drive.Id);
        if (drive.IsArchived && !odinContext.Caller.HasMasterKey)
        {
            throw new OdinClientException("Drive is archived", OdinClientErrorCode.InvalidDrive);
        }
    }

    /// <summary>
    /// The drive's app's own circle, as for its circle key (PR 2): never an owner circle, never another app's.
    /// </summary>
    private async Task<CircleRecord> GetCircleCallerMayShareWithAsync(StorageDrive drive, Guid circleId, IOdinContext odinContext)
    {
        var circle = await circleMembership.GetCircleCallerMayManageAsync(new GuidId(circleId), "share files with", odinContext);
        if (SystemAppConstants.IsOwnerConsole(circle.AppId) || circle.AppId != drive.AppId)
        {
            throw new OdinSecurityException($"Circle {circleId} isn't the circle of drive {drive.Id}'s app");
        }

        return circle;
    }

    /// <summary>
    /// The circle a share is for: enabled, granting the drive Read and Write (decision 1), and holding its key, which
    /// the file key is locked with.
    /// </summary>
    private async Task AssertCircleMayShareAsync(StorageDrive drive, Guid circleId, IOdinContext odinContext)
    {
        var circle = await GetCircleCallerMayShareWithAsync(drive, circleId, odinContext);
        var definition = CircleDefinitionService.FromRecord(circle);
        if (definition.Disabled)
        {
            throw new OdinClientException($"Circle {circleId} is disabled", OdinClientErrorCode.ArgumentError);
        }

        const DrivePermission readWrite = DrivePermission.Read | DrivePermission.Write;
        if (definition.DriveGrants?.Any(g => g.PermissionedDrive.Drive == drive.TargetDriveInfo &&
                                             (g.PermissionedDrive.Permission & readWrite) == readWrite) != true)
        {
            throw new OdinClientException($"Circle {circleId} doesn't grant the drive Read and Write", OdinClientErrorCode.ArgumentError);
        }

        if (circle.appKeyLockedShareKey == null)
        {
            throw new OdinClientException($"Circle {circleId} has no share key yet", OdinClientErrorCode.CircleShareKeyNotSet);
        }
    }

    private static void AssertShape(ShareRequest share, UnixTimeUtc now)
    {
        if ((share.Permissions & DrivePermission.Read) == 0 || (share.Permissions & ~ShareBits) != 0)
        {
            throw new OdinClientException($"A share holds Read, and otherwise only Write, React and Comment, not {share.Permissions}",
                OdinClientErrorCode.ArgumentError);
        }

        if (share.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            throw new OdinClientException("A share expires in the future", OdinClientErrorCode.ArgumentError);
        }

        if (share.LockedFileKey is { } key && (key.Length != LockedFileKeyLength || key[0] != CircleShareKeyService.FormatVersion))
        {
            throw new OdinClientException(
                $"A locked file key is {LockedFileKeyLength} bytes of format version {CircleShareKeyService.FormatVersion}",
                OdinClientErrorCode.InvalidShareKey);
        }
    }

    // A locked file key with each share exactly when the file is encrypted
    private static void AssertKeysFit(bool encrypted, IEnumerable<ShareRequest> shares, string file)
    {
        if (shares.Any(s => (s.LockedFileKey != null) != encrypted))
        {
            throw new OdinClientException(encrypted
                ? $"{file} is encrypted: each share carries its file key, locked with the circle's key"
                : $"{file} isn't encrypted: its shares carry no file key", OdinClientErrorCode.InvalidShareKey);
        }
    }

    /// <summary>
    /// The file, read with its row locked: an active standard file; a locked file key with each share exactly when it
    /// is encrypted; and the lift of an Owner file to Connected, in the column and the header alike (decision A).
    /// </summary>
    private static (int requiredSecurityGroup, string hdrServerData)? CheckFileAndLift(DriveMainIndexRecord record,
        IReadOnlyCollection<ShareRequest> shares)
    {
        var header = ServerFileHeader.FromDriveMainIndexRecord(record);
        if (header.FileMetadata.FileState != FileState.Active || header.ServerMetadata.FileSystemType != FileSystemType.Standard)
        {
            throw new OdinClientException($"File {record.fileId} is deleted or isn't a file", OdinClientErrorCode.InvalidFile);
        }

        AssertKeysFit(header.FileMetadata.IsEncrypted, shares, $"File {record.fileId}");

        var acl = header.ServerMetadata.AccessControlList;
        if (acl.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            return null;
        }

        acl.RequiredSecurityGroup = SecurityGroupType.Connected;
        return ((int)SecurityGroupType.Connected, OdinSystemSerializer.Serialize(new ServerMetadataDto(header.ServerMetadata)));
    }
}

/// <summary>A share to write: the file, the circle, what its members may do, until when, and the file key locked for it.</summary>
public class ShareRequest
{
    public Guid FileId { get; init; }
    public Guid CircleId { get; init; }
    public DrivePermission Permissions { get; init; }
    public UnixTimeUtc? ExpiresAt { get; init; }

    /// <summary>For an encrypted file: its file key locked with the circle's key; null for an unencrypted one.</summary>
    public byte[]? LockedFileKey { get; init; }
}

/// <summary>A share as stored, without its locked file key, which only a member needs.  No bits: withdrawn.</summary>
public class ShareRow
{
    public Guid CircleId { get; init; }
    public DrivePermission Permissions { get; init; }
    public UnixTimeUtc? ExpiresAt { get; init; }
    public UnixTimeUtc Created { get; init; }
    public UnixTimeUtc Modified { get; init; }

    internal static ShareRow From(DriveAclIndexRecord row) => new()
    {
        CircleId = row.aclMemberId, Permissions = (DrivePermission)row.permissions, ExpiresAt = row.expiresAt,
        Created = row.created, Modified = row.modified
    };
}
