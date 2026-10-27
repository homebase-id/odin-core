#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;

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
    private static KeyHeader OpenForWrite(EncryptedKeyHeader? sent, StorageDrive drive, ref SensitiveByteArray sharedSecret,
        IOdinContext odinContext)
    {
        if (sent == null)
        {
            throw new OdinClientException("An encrypted file needs its key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, sent.Type);
        if (sent.IsSealed())
        {
            if (drive.IsZeroKnowledge && CallerHoldsDriveKey(drive.Id, odinContext))
            {
                ThrowNotItsOwnKeyHeader();
            }

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
    public static async Task<KeyHeader> OpenForWriteAsync(EncryptedKeyHeader? sent, Guid driveId, IDriveManager driveManager,
        IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(driveId, failIfInvalid: true);
        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        return OpenForWrite(sent, drive, ref sharedSecret, odinContext);
    }

    /// <summary>
    /// Which key header a peer may send to <paramref name="drive"/>, refused when the transfer arrives, before
    /// anything is staged or the sender is told it was accepted: never one locked with this identity's drive
    /// key (zkDrive), which a peer doesn't hold, and only what the drive takes (<see cref="AssertDriveTakes"/>).
    /// Asked for an encrypted file only.  The local rule is <see cref="OpenForWrite"/>; what an update or an
    /// overwrite may replace is <see cref="AssertMayReplace"/>'s, where the existing file is known.
    /// </summary>
    public static void AssertPeerMayWrite(StorageDrive drive, EncryptedKeyHeader? keyHeader)
    {
        if (keyHeader?.IsZkDrive() == true)
        {
            throw new OdinClientException("A peer can't lock a key header with this drive's key", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, keyHeader?.Type ?? EncryptionType.Aes);
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
        if (replacement is ClientLockedKeyHeader { Type: EncryptionType.Sealed } &&
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
    /// <see cref="Deposits.AssertCanHold"/> says.
    /// </summary>
    public static void AssertDriveTakes(StorageDrive drive, EncryptionType type)
    {
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
