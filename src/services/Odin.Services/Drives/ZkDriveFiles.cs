#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Drives;

/// <summary>
/// A zero-knowledge drive's files are locked by a client, with a key the server doesn't hold: an owner
/// device locked the key header with the drive's key (<see cref="EncryptionType.ZkDrive"/>), or someone
/// without it sealed the key header to the drive's write-only public key (<see cref="EncryptionType.Sealed"/>,
/// a deposit).  The server stores either as received and returns it as stored; it never opens or re-locks
/// one (docs/zero-knowledge-drive-api-plan.md, steps 1 and 2).  These are the rules for both.
/// </summary>
public static class ZkDriveFiles
{
    /// <summary>A file whose key header a client locked: <see cref="EncryptedKeyHeader.IsLockedByClient"/>.</summary>
    /// <remarks>By its key header alone: a deleted file's tombstone keeps the key header but not IsEncrypted.</remarks>
    public static bool IsClientLocked(ServerFileHeader? header) => header?.EncryptedKeyHeader?.IsLockedByClient() == true;

    /// <summary>
    /// The key header a local caller (the owner, an app) sent to write to <paramref name="drive"/>, decided
    /// by its type before anything is opened (<see cref="AssertDriveTakes"/>).  A zero-knowledge drive's own
    /// key header comes only from a caller holding its key.  A sealed one is a deposit: not from a caller
    /// holding the drive's key, which writes <see cref="EncryptionType.ZkDrive"/> instead; anyone else with Write
    /// may deposit, on a zero-knowledge drive or on the transient drive to send it.  What it may replace in an
    /// update or an overwrite is <see cref="AssertMayReplace"/>'s.
    /// </summary>
    private static KeyHeader OpenForWrite(EncryptedKeyHeader? sent, StorageDrive drive, FileSystemType fileSystem,
        ref SensitiveByteArray sharedSecret, IOdinContext odinContext)
    {
        if (sent == null)
        {
            throw new OdinClientException("An encrypted file needs its key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, sent.Type, fileSystem);
        if (sent.IsFileKey())
        {
            sent.AssertValidFileKey();
            return new ClientLockedKeyHeader(sent);
        }

        if (sent.IsShare())
        {
            sent.AssertKeepsKey();
            return new ClientLockedKeyHeader(sent);
        }

        if (sent.IsSealed())
        {
            if (drive.IsZeroKnowledge && CallerHoldsDriveKey(drive.Id, odinContext))
            {
                ThrowNotItsOwnKeyHeader();
            }

            AssertSealedToItsKey(drive, sent);
            return new ClientLockedKeyHeader(sent);
        }

        if (!sent.IsZkDrive())
        {
            return sent.DecryptAesToKeyHeader(ref sharedSecret);
        }

        AssertCallerHoldsDriveKey(drive.Id, odinContext);
        sent.AssertValidZkDrive();
        return new ClientLockedKeyHeader(sent);
    }

    /// <summary>The key header a client sent, for the drive it writes to (<see cref="OpenForWrite"/>).</summary>
    public static async Task<KeyHeader> OpenForWriteAsync(EncryptedKeyHeader? sent, Guid driveId, FileSystemType fileSystem,
        IDriveManager driveManager, IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(driveId, failIfInvalid: true);
        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        return OpenForWrite(sent, drive, fileSystem, ref sharedSecret, odinContext);
    }

    /// <summary>
    /// Which key header a peer may send to <paramref name="drive"/>, refused when the transfer arrives, before
    /// anything is staged or the sender is told it was accepted: never one locked with this identity's drive
    /// key (zkDrive), which a peer doesn't hold, and only what the drive takes (<see cref="AssertDriveTakes"/>).
    /// Asked for an encrypted file only.  The local rule is <see cref="OpenForWrite"/>; what an update or an
    /// overwrite may replace is <see cref="AssertMayReplace"/>'s, where the existing file is known.
    /// </summary>
    public static void AssertPeerMayWrite(StorageDrive drive, EncryptedKeyHeader? keyHeader, FileSystemType fileSystem)
    {
        if (keyHeader?.IsZkDrive() == true)
        {
            throw new OdinClientException("A peer can't lock a key header with this drive's key", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, keyHeader?.Type ?? EncryptionType.Aes, fileSystem);
        if (keyHeader?.IsSealed() == true)
        {
            AssertSealedToItsKey(drive, keyHeader);
        }

        if (keyHeader?.IsFileKey() == true)
        {
            keyHeader.AssertValidFileKey();
        }
    }

    /// <summary>
    /// Which key header a peer's update may send to <paramref name="drive"/>: on a Selective Share drive, a member's
    /// keeping the shared file's key (<see cref="EncryptedKeyHeader.AssertKeepsKey"/>, decision C); otherwise what a
    /// peer may write (<see cref="AssertPeerMayWrite"/>).
    /// </summary>
    public static void AssertPeerMayUpdate(StorageDrive drive, EncryptedKeyHeader? keyHeader, FileSystemType fileSystem)
    {
        if (keyHeader?.IsShare() != true)
        {
            AssertPeerMayWrite(drive, keyHeader, fileSystem);
            return;
        }

        if (!drive.IsSelectiveShare)
        {
            throw new OdinClientException("Only a SelectiveShare drive's member keeps a file's key", OdinClientErrorCode.InvalidKeyHeader);
        }

        keyHeader.AssertKeepsKey();
    }

    /// <summary>
    /// A member's update keeping a shared file's key (decision C), as the key header to store: the file's own, locked
    /// with the drive's key by an owner device, with the member's new IV.  Sound for a version 1 key header, which locks
    /// <c>IV ‖ key</c> under the IV it shows, so the IV it shows is the one that counts (security-model.md); key header
    /// version 2 (#2024) would need this revisited.  A file the member deposited takes a new sealed key header instead.
    /// Any other key header is returned as it is.
    /// </summary>
    public static KeyHeader KeepFileKey(ServerFileHeader existing, KeyHeader update)
    {
        if (update is not ClientLockedKeyHeader { Type: EncryptionType.Share } keepKey)
        {
            return update;
        }

        var stored = existing.EncryptedKeyHeader;
        if (stored?.IsZkDrive() != true)
        {
            throw new OdinClientException("Only a file an owner device locked keeps its key on a member's update; " +
                                          "a deposit takes a new sealed key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        return new ClientLockedKeyHeader(new EncryptedKeyHeader
        {
            EncryptionVersion = stored.EncryptionVersion,
            Type = EncryptionType.ZkDrive,
            Iv = keepKey.Iv,
            EncryptedAesKey = stored.EncryptedAesKey
        });
    }

    /// <summary>
    /// A deposit must be sealed to the drive's write-only key, which the seal names by its key id: one sealed to any
    /// other key could never be opened.  Refused rather than stored, so the sender learns its copy of the public key
    /// is out of date (docs/drive-public-keys-plan.md).  Not on the transient drive, where a sender's deposit waits to
    /// go to another identity's drive.  Its shape first: the update paths check it nowhere else, and a malformed one
    /// is told so rather than that its key is unknown.
    /// </summary>
    private static void AssertSealedToItsKey(StorageDrive drive, EncryptedKeyHeader sealedKeyHeader)
    {
        if (SystemDriveConstants.IsTransientTempDrive(drive.Id))
        {
            return;
        }

        sealedKeyHeader.AssertValidSealed();
        if (!drive.IsSealedToWriteOnlyKey(sealedKeyHeader.EncryptedAesKey))
        {
            throw new OdinClientException("Sealed to a key this drive doesn't hold; fetch its public key again",
                OdinClientErrorCode.SealedToUnknownKey);
        }
    }

    /// <summary>
    /// Who may transform deposits into the drive's own key headers: a caller holding the key of a zero-knowledge drive,
    /// which is how it opened the deposits and locked their file keys again (docs/zero-knowledge-drives.md).  Never on the
    /// Shard Recovery drive: the server releases a friend's shard only as the sealed deposit it holds
    /// (<c>ShamirBaseService</c>).
    /// </summary>
    public static void AssertCallerMayTransform(StorageDrive drive, IOdinContext odinContext)
    {
        if (!drive.IsZeroKnowledge)
        {
            throw new OdinClientException("Only a zero-knowledge drive's deposits are transformed",
                OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
        }

        AssertCallerHoldsDriveKey(drive.Id, odinContext);

        // An exception for Shamir, not a rule of drives.  Before it releases a gated friend's shard, the server asks
        // whether the friend's browser can still open it, and answers from the seal: a sealed key header names the
        // write-only key it was sealed to, which a reset with a new master key replaces (ShamirBaseService.CanReleaseAsync).
        // A zkDrive key header names no key, so a transformed shard could no longer be released.  The real fix is in
        // Shamir: it keeps its own record of which key a shard is locked under, rather than reading it off the key
        // header, and then this exception goes and shards transform like any deposit.
        if (drive.Id == WellKnownAppDrives.ShardRecoveryDrive.Alias.Value)
        {
            throw new OdinClientException("Shards stay sealed: the server releases them only as they were deposited",
                OdinClientErrorCode.InvalidDrive);
        }
    }

    /// <summary>
    /// The key header a transform stores in place of <paramref name="stored"/>: a deposit's sealed one, replaced by the
    /// <see cref="EncryptionType.ZkDrive"/> one <paramref name="sent"/> with its IV, since the content was encrypted under
    /// it (payloads keep their own).  The server can't check that it locks the same file key; the client opens what it
    /// locked before sending it.
    /// </summary>
    public static EncryptedKeyHeader TransformedKeyHeader(EncryptedKeyHeader? stored, EncryptedKeyHeader? sent)
    {
        if (stored?.IsSealed() != true)
        {
            throw new OdinClientException("Only a sealed key header is transformed", OdinClientErrorCode.NotSealed);
        }

        if (sent?.IsZkDrive() != true)
        {
            throw new OdinClientException("A transform sends a key header locked with the drive's key (zkDrive)",
                OdinClientErrorCode.InvalidKeyHeader);
        }

        sent.AssertValidZkDrive();
        if (stored.Iv == null || !ByteArrayUtil.EquiByteArrayCompare(sent.Iv, stored.Iv))
        {
            throw new OdinClientException("A transform keeps the key header's IV, which the content was encrypted under",
                OdinClientErrorCode.InvalidKeyHeader);
        }

        return sent;
    }

    /// <summary>
    /// What a sealed key header may replace, in an update or an overwrite of <paramref name="existing"/>: only
    /// another sealed one (a deposit's sender updating it).  Never a key header locked with the drive's key
    /// (zkDrive): a file a key holder has taken over doesn't go back to sealed.  Never an aes one either: the
    /// file key only the server could open would be lost.  A sealed one carries no payloads
    /// (<see cref="Deposits"/>).  Asked inside the update and overwrite paths, which keep their version-tag and IV
    /// checks (docs/zero-knowledge-drive-api-plan.md, 2.4).
    /// </summary>
    public static void AssertMayReplace(ServerFileHeader existing, KeyHeader? replacement)
    {
        if (replacement is ClientLockedKeyHeader { IsSealed: true } &&
            existing.FileMetadata.IsEncrypted && existing.EncryptedKeyHeader?.IsSealed() != true)
        {
            throw new OdinClientException("A sealed key header replaces only a sealed one: never one locked with the drive's key, " +
                                          "or by the server", OdinClientErrorCode.DepositCannotBeUpdated);
        }
    }

    /// <summary>
    /// Which drive takes which key header: a zero-knowledge drive refuses one locked with the shared secret
    /// (the server would see the file key, and store a key header nobody could open), and any other drive
    /// refuses a zero-knowledge drive's own.  A deposit (<see cref="EncryptionType.Sealed"/>) goes where
    /// <see cref="Deposits.AssertCanHold"/> says.  On a Selective Share drive an encrypted comment takes only
    /// <see cref="EncryptionType.FileKey"/>, the key of the file it comments on, so whoever may read that file reads
    /// its comments; nothing else does (docs/selective-share-pr3-plan.md, section 10).
    /// </summary>
    public static void AssertDriveTakes(StorageDrive drive, EncryptionType type, FileSystemType fileSystem)
    {
        var selectiveShareComment = drive.IsSelectiveShare && fileSystem == FileSystemType.Comment;
        if (type == EncryptionType.FileKey)
        {
            // Staged on the transient drive on its way to the owner's (a member's comment)
            if (!selectiveShareComment && !SystemDriveConstants.IsTransientTempDrive(drive.Id))
            {
                throw new OdinClientException("Only a comment on a SelectiveShare drive is encrypted with its file's key",
                    OdinClientErrorCode.InvalidKeyHeader);
            }

            return;
        }

        if (selectiveShareComment)
        {
            throw new OdinClientException("A comment on a SelectiveShare drive is encrypted with the key of the file it comments on",
                OdinClientErrorCode.InvalidKeyHeader);
        }

        if (type == EncryptionType.Share)
        {
            // Never a file's key header.  A member's update keeping a shared file's key is staged on the transient
            // drive on its way to the owner's (AssertPeerMayUpdate)
            if (!SystemDriveConstants.IsTransientTempDrive(drive.Id))
            {
                throw new OdinClientException("A share's key goes only to a member: it is never a file's key header",
                    OdinClientErrorCode.InvalidKeyHeader);
            }

            return;
        }

        if (type == EncryptionType.Sealed)
        {
            Deposits.AssertCanHold(drive);
            return;
        }

        if (drive.IsZeroKnowledge && type != EncryptionType.ZkDrive)
        {
            ThrowNotItsOwnKeyHeader();
        }

        if (!drive.IsZeroKnowledge && type == EncryptionType.ZkDrive)
        {
            throw new OdinClientException("Only a zero-knowledge drive takes a key header locked with its own key",
                OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
        }
    }

    /// <summary>
    /// Whether the caller holds zero-knowledge drive <paramref name="driveId"/>'s key, so may read and write its
    /// own files: the owner console, which opens the drive's key with the zero-knowledge master key, or a device
    /// that was handed its app's zero-knowledge key at sign-in, of an app holding a locked copy of this drive's key
    /// (docs/zero-knowledge-drive-api-plan.md, step 3).  Any other device of an app, an older build among them,
    /// is not: an app that can't open a key header must not be handed one (chat-kmp stalls its whole sync on it),
    /// and one that can't lock a key header can only write junk over the owner's.
    /// </summary>
    public static bool CallerHoldsDriveKey(Guid driveId, IOdinContext odinContext)
    {
        var caller = odinContext.Caller;
        return caller?.HasMasterKey == true ||
               caller?.OdinClientContext is { HoldsZkAppKey: true, ZkDriveKeys: { } keys } && keys.ContainsKey(driveId);
    }

    /// <summary>
    /// Whether <paramref name="header"/> may reach the caller: a client-locked file only reaches one
    /// holding the drive's key, whatever else it holds (a grant from before the drive was converted still
    /// carries the old key; an app that can't open a sealed or zkDrive key header stalls on it), and the
    /// system's jobs (a file's TTL, the outbox).  On the transient drive a deposit is only staged to be
    /// sent, and reaches any caller with Read there, not only its writer (an app sends its own; another app
    /// with transit sees the sealed header and plain metadata, as every reader of a deposit did before step
    /// 2).  Says nothing about any other file.  Every read and write of a stored header asks this, by query,
    /// socket or id, as part of <see cref="Authorization.Acl.IDriveAclAuthorizationService.CallerMayReadAsync"/>.
    /// </summary>
    /// <remarks>
    /// It also gates the server's own lookups made in a caller's context, such as an inbox drained by an app:
    /// those miss a client-locked file.  Hiding it where responses are built instead is the deeper fix
    /// (docs/zero-knowledge-drive-api-plan.md, "Open from step 2's review").
    /// </remarks>
    public static bool VisibleTo(ServerFileHeader? header, IOdinContext odinContext) =>
        !IsClientLocked(header) || SystemDriveConstants.IsTransientTempDrive(header!.FileMetadata.File.DriveId) ||
        CallerHoldsDriveKey(header.FileMetadata.File.DriveId, odinContext) || odinContext.Caller?.IsSystem == true;

    /// <summary>Refuses a file the caller may not see (<see cref="VisibleTo"/>), as a read by id does</summary>
    public static void AssertVisibleTo(ServerFileHeader header, IOdinContext odinContext)
    {
        if (!VisibleTo(header, odinContext))
        {
            throw new OdinSecurityException("No access permitted to the file");
        }
    }

    private static void AssertCallerHoldsDriveKey(Guid driveId, IOdinContext odinContext)
    {
        if (!CallerHoldsDriveKey(driveId, odinContext))
        {
            ThrowNoAccess(driveId);
        }
    }

    private static void ThrowNoAccess(Guid driveId) => throw new OdinSecurityException($"No access permitted to drive {driveId}");

    /// <summary>
    /// An encrypted update of a client-locked file (sealed or zkDrive) takes a client-locked key header with a
    /// new IV, as for any encrypted file: a zkDrive one from a device holding the drive's key, or a sealed one
    /// over a sealed file (<see cref="AssertMayReplace"/>, asked here first for any encrypted file).  The server compares the IVs both key headers show; it can't check that
    /// the file key stayed the same, which is the writer's to keep, or the payloads no longer open.  Callers
    /// refuse a change of encryption first; any other file never gets a client-locked key header
    /// (<see cref="AssertDriveTakes"/>).
    /// </summary>
    public static void AssertFitsUpdate(ServerFileHeader existing, KeyHeader update)
    {
        AssertMayReplace(existing, update);
        if (!IsClientLocked(existing))
        {
            return;
        }

        if (update is not ClientLockedKeyHeader)
        {
            ThrowNotItsOwnKeyHeader();
        }

        KeyHeader.AssertIvRotated(update.Iv, existing.EncryptedKeyHeader.Iv);
    }

    /// <summary>
    /// A zero-knowledge drive sends nothing: its own files need the drive's key, and the server holds no
    /// storage key to re-lock anything else for a recipient.  Writes refuse recipients before they store
    /// anything; Chat will send through deposits (docs/zero-knowledge-drive-api-plan.md, step 2).
    /// </summary>
    public static void AssertNotSent<T>(StorageDrive drive, IEnumerable<T>? recipients)
    {
        if (drive.IsZeroKnowledge && recipients?.Any() == true)
        {
            ThrowCannotBeSent();
        }
    }

    private static void ThrowCannotBeSent() =>
        throw new OdinClientException("A zero-knowledge drive's file can't be sent", OdinClientErrorCode.ZkDriveFileCannotBeSent);

    private static void ThrowNotItsOwnKeyHeader() =>
        throw new OdinClientException("A zero-knowledge drive takes only key headers locked with its own key",
            OdinClientErrorCode.DriveIsZeroKnowledge);
}
