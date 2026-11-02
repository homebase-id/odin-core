#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Mediator;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Drives.Shares;

/// <summary>
/// A file's shares on a Selective Share drive: each is the file's <c>DriveAclIndex</c> row for a circle, with what its
/// members may do, an expiry and the file key locked with the circle's key, which the server never opens
/// (docs/selective-share-pr3-plan.md, section 3).  Written by the owner console or by the owner's app that owns both the
/// drive and the circle.
/// </summary>
public class DriveShareService(
    IdentityDatabase db,
    IDriveManager driveManager,
    CircleMembershipService circleMembership,
    IMediator mediator,
    ILogger<DriveShareService> logger)
{
    public const int MaxSharesPerRequest = 100;

    /// <summary>AES-GCM under the circle key: format (1) ‖ nonce (12) ‖ file key (16) ‖ tag (16) (decision 7).</summary>
    public const int LockedFileKeyLength = 45;

    /// <summary>The bits a share may hold, each narrowing the same bit of its circle's drive grant (docs/file-access.md).</summary>
    public const DrivePermission ShareBits = DrivePermission.Read | DrivePermission.Write | DrivePermission.React | DrivePermission.Comment;

    public async Task<List<ShareRow>> GetSharesAsync(Guid driveId, Guid fileId, IOdinContext odinContext)
    {
        var drive = await GetDriveCallerMayShareOnAsync(driveId, odinContext);
        odinContext.PermissionsContext.AssertCanReadDrive(drive.Id);

        return (await RowsOfAsync(drive, fileId)).Select(ShareRow.From).ToList();
    }

    /// <summary>
    /// Writes the shares, all or none: each is checked before anything is written, the files in one transaction.
    /// A file shared while <c>Owner</c> is lifted to <c>Connected</c> (decision A); every file's <c>modified</c>
    /// moves and its version tag stays (decision 6).
    /// </summary>
    public async Task WriteSharesAsync(Guid driveId, List<ShareRequest>? shares, IOdinContext odinContext)
    {
        AssertShapes(shares, s => (s.FileId, s.CircleId), "a file shared with a circle once");
        var drive = await GetDriveForSharesAsync(driveId, shares!, odinContext);

        await using var tx = await db.BeginStackedTransactionAsync();
        // Files locked in one order, so two batches over the same files can't deadlock
        var missing = await db.MainIndexMetaCached.WriteSharesAsync(drive.Id, shares!.GroupBy(s => s.FileId).OrderBy(file => file.Key)
            .Select(file => new FileShares(file.Key, ToRows(file), record => CheckFileAndLift(record, file.ToList()))));
        if (missing is { } missingFile)
        {
            throw new OdinClientException($"No file {missingFile} on the drive", OdinClientErrorCode.InvalidFile);
        }

        tx.Commit();
        foreach (var fileId in shares!.Select(s => s.FileId).Distinct())
        {
            await PublishChangedAsync(new InternalDriveFileId(drive.Id, fileId), odinContext);
        }
    }

    /// <summary>
    /// A share write raises the file's ordinary file-changed event (decided 2026-11-02, section 9): owner callers'
    /// devices hear it as any change, and a member's socket gets the file through their share or, once it ends, its
    /// tombstone (<c>DriveEventMessage.MessageForAsync</c>).  A share changes no content, so the feed and the
    /// reaction previews have nothing to do.  The write stands whatever the event does.
    /// </summary>
    private async Task PublishChangedAsync(InternalDriveFileId file, IOdinContext odinContext)
    {
        try
        {
            var record = await db.DriveMainIndexCached.GetAsync(file.DriveId, file.FileId);
            if (record == null)
            {
                return;
            }

            await mediator.Publish(new DriveFileChangedNotification
            {
                File = file,
                ServerFileHeader = ServerFileHeader.FromDriveMainIndexRecord(record),
                OdinContext = odinContext,
                IgnoreFeedDistribution = true,
                IgnoreReactionPreviewCalculation = true
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "The event for a share of file {file} on drive {drive}", file.FileId, file.DriveId);
        }
    }

    /// <summary>
    /// A new file's upload carrying its shares (section 4): the same checks as <see cref="WriteSharesAsync"/>, before
    /// anything is stored.  The shares name no file; they are the upload's.
    /// </summary>
    public async Task AssertUploadMayShareAsync(Guid driveId, List<ShareRequest> shares, bool encrypted, IOdinContext odinContext)
    {
        AssertSentShares(shares, encrypted);
        await GetDriveForSharesAsync(driveId, shares, odinContext);
    }

    /// <summary>
    /// Shares a member's server sends on with a new file (a transient upload): their shape only, as the member's server
    /// knows nothing of the owner's circles.  The owner's server checks the rest (<see cref="AssertMemberMayShareAsync"/>).
    /// </summary>
    public static void AssertSentShares(List<ShareRequest> shares, bool encrypted)
    {
        if (shares.Any(s => s.FileId != Guid.Empty))
        {
            throw new OdinClientException("A new file's shares are for that file: they name no file", OdinClientErrorCode.ArgumentError);
        }

        AssertShapes(shares, s => s.CircleId, "one per circle");
        AssertKeysFit(encrypted, shares, "The file");
    }

    /// <summary>
    /// A member's shares for the file they add to a shared folder (docs/selective-share-pr3-plan.md, section 7): only for
    /// circles they are in, each the drive's app's own, enabled, granting the drive every bit the share asks and holding its
    /// key.
    /// </summary>
    public async Task AssertMemberMayShareAsync(StorageDrive drive, List<ShareRequest> shares, bool encrypted, IOdinContext odinContext)
    {
        AssertSentShares(shares, encrypted);
        var callersCircles = odinContext.Caller.Circles?.Select(c => c.Value).ToHashSet() ?? [];
        foreach (var share in shares)
        {
            var circle = callersCircles.Contains(share.CircleId) ? await db.CircleCached.GetAsync(share.CircleId) : null;
            if (circle == null)
            {
                throw new OdinSecurityException($"A member shares a file only with a circle they are in, not {share.CircleId}");
            }

            AssertIsTheDrivesAppsCircle(drive, circle);
            AssertCircleFits(drive, circle, share.Permissions);
        }
    }

    /// <summary>The upload's shares, written in its header's transaction, so a file is never seen half shared.</summary>
    public WriteSecondDatabaseRowBase UploadSharesWriter(InternalDriveFileId file, List<ShareRequest> shares) =>
        new WriteUploadShares(db, file, ToRows(shares));

    private sealed class WriteUploadShares(IdentityDatabase db, InternalDriveFileId file, List<DriveAclIndexRecord> rows)
        : WriteSecondDatabaseRowBase
    {
        public override async Task<int> ExecuteAsync() =>
            await db.MainIndexMetaCached.WriteSharesAsync(file.DriveId, [new FileShares(file.FileId, rows, _ => null)]) == null ? 1 : 0;
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

        var row = (await RowsOfAsync(drive, fileId)).FirstOrDefault(r => r.aclMemberId == circleId);
        if (row == null)
        {
            throw new OdinClientException($"File {fileId} isn't shared with circle {circleId}", OdinClientErrorCode.ArgumentError);
        }

        if (row.permissions == 0)
        {
            return;
        }

        await db.MainIndexMetaCached.WriteSharesAsync(drive.Id,
            [new FileShares(fileId, [new DriveAclIndexRecord { aclMemberId = circleId, permissions = 0 }], _ => null)]);
        await PublishChangedAsync(new InternalDriveFileId(drive.Id, fileId), odinContext);
    }

    /// <summary>The drive, once the caller may write shares on it for each circle of <paramref name="shares"/>.</summary>
    private async Task<StorageDrive> GetDriveForSharesAsync(Guid driveId, List<ShareRequest> shares, IOdinContext odinContext)
    {
        var drive = await GetDriveCallerMayShareOnAsync(driveId, odinContext);
        AssertCallerMayWrite(drive, odinContext);
        foreach (var circleShares in shares.GroupBy(s => s.CircleId))
        {
            var circle = await GetCircleCallerMayShareWithAsync(drive, circleShares.Key, odinContext);
            foreach (var share in circleShares)
            {
                AssertCircleFits(drive, circle, share.Permissions);
            }
        }

        return drive;
    }

    /// <summary>Between one and <see cref="MaxSharesPerRequest"/> shares, each at most once by <paramref name="key"/>, each well formed.</summary>
    private static void AssertShapes<TKey>(List<ShareRequest>? shares, Func<ShareRequest, TKey> key, string once)
    {
        if (shares == null || shares.Count == 0 || shares.Count > MaxSharesPerRequest || shares.DistinctBy(key).Count() != shares.Count)
        {
            throw new OdinClientException($"Between 1 and {MaxSharesPerRequest} shares per request, {once}", OdinClientErrorCode.ArgumentError);
        }

        var now = UnixTimeUtc.Now();
        foreach (var share in shares)
        {
            AssertShape(share, now);
        }
    }

    // Every row of the file: its shares, withdrawn ones included
    private async Task<List<DriveAclIndexRecord>> RowsOfAsync(StorageDrive drive, Guid fileId) =>
        (await db.DriveAclIndex.GetRowsAsync(drive.Id, [fileId], [], null)).Select(r => r.Row).ToList();

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

        if (!SelectiveShareDrives.CallerManagesShares(drive, odinContext.Caller))
        {
            throw new OdinSecurityException($"Only the owner console or the app that owns drive {driveId} may share its files");
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
        AssertIsTheDrivesAppsCircle(drive, circle);
        return circle;
    }

    private static void AssertIsTheDrivesAppsCircle(StorageDrive drive, CircleRecord circle) =>
        CircleMembershipService.AssertCircleIsTheApps(new GuidId(circle.circleId), circle.AppId, drive.AppId,
            $"share drive {drive.Id}'s files with");

    /// <summary>
    /// The circle a share is for: enabled, granting the drive every bit the share asks, since a share row narrows its
    /// circle's grant and never widens it (docs/file-access.md, "A share row narrows its circle's grant"), and holding
    /// its key, which the file key is locked with.
    /// </summary>
    private static void AssertCircleFits(StorageDrive drive, CircleRecord circle, DrivePermission asked)
    {
        var circleId = circle.circleId;
        var definition = CircleDefinitionService.FromRecord(circle);
        if (definition.Disabled)
        {
            throw new OdinClientException($"Circle {circleId} is disabled", OdinClientErrorCode.ArgumentError);
        }

        var granted = definition.DriveGrants?.Where(g => g.PermissionedDrive.Drive == drive.TargetDriveInfo)
            .Aggregate((DrivePermission)0, (bits, g) => bits | g.PermissionedDrive.Permission) ?? 0;
        if ((granted & asked) != asked)
        {
            throw new OdinClientException($"Circle {circleId} doesn't grant the drive {asked & ~granted}, which a share asks",
                OdinClientErrorCode.ArgumentError);
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

        if (!SelectiveShareDrives.LiftForShares(header.ServerMetadata.AccessControlList))
        {
            return null;
        }

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
