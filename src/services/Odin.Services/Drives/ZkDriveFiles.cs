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
    /// key header comes only from a caller holding its key.  A sealed one is a deposit, written whole: never
    /// in an update (<paramref name="isUpdate"/>), and not by a caller holding the drive's key, which writes
    /// <see cref="EncryptionType.ZkDrive"/> instead; anyone else with Write may deposit, on a zero-knowledge
    /// drive or on the transient drive to send it.
    /// </summary>
    private static KeyHeader OpenForWrite(EncryptedKeyHeader? sent, StorageDrive drive, bool isUpdate, ref SensitiveByteArray sharedSecret,
        IOdinContext odinContext)
    {
        if (sent == null)
        {
            throw new OdinClientException("An encrypted file needs its key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, sent.Type);
        if (sent.IsSealed())
        {
            if (isUpdate)
            {
                throw new OdinClientException("A deposit is written whole; a sealed key header can't update a file",
                    OdinClientErrorCode.DepositCannotBeUpdated);
            }

            if (drive.IsZeroKnowledge && CallerHoldsDriveKey(odinContext))
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
    /// <param name="isUpdate">An update of an existing file (an update, a metadata-only upload), not a new file or a full overwrite</param>
    public static async Task<KeyHeader> OpenForWriteAsync(EncryptedKeyHeader? sent, Guid driveId, bool isUpdate,
        IDriveManager driveManager, IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(driveId, failIfInvalid: true);
        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        return OpenForWrite(sent, drive, isUpdate, ref sharedSecret, odinContext);
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
    /// Whether the caller holds a zero-knowledge drive's key, so may read and write its own files.  Only
    /// the owner console, for now: it opens the drive's key with the zero-knowledge master key.  An app
    /// learns the drive's key only at consent (docs/zero-knowledge-drives.md, item 4); an app that can't
    /// open a key header must not be handed one (chat-kmp stalls its whole sync on it), and one that
    /// can't lock a key header can only write junk over the owner's.
    /// </summary>
    public static bool CallerHoldsDriveKey(IOdinContext odinContext) => odinContext.Caller?.HasMasterKey == true;

    /// <summary>
    /// Whether <paramref name="header"/> may reach the caller: a client-locked file only reaches one
    /// holding the drive's key, whatever else it holds (a grant from before the drive was converted still
    /// carries the old key; an app that can't open a sealed or zkDrive key header stalls on it), and the
    /// system's jobs (a file's TTL, the outbox).  On the transient drive a deposit is only staged to be
    /// sent, by whoever wrote it, and reaches any caller there (an app sends its own).  Says nothing about
    /// any other file.  Every read and write of a stored header asks this, by query, socket or id, as part of
    /// <see cref="Authorization.Acl.IDriveAclAuthorizationService.CallerMayReadAsync"/>.
    /// </summary>
    public static bool VisibleTo(ServerFileHeader? header, IOdinContext odinContext) =>
        !IsClientLocked(header) || SystemDriveConstants.IsTransientTempDrive(header!.FileMetadata.File.DriveId) ||
        CallerHoldsDriveKey(odinContext) || odinContext.Caller?.IsSystem == true;

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
        if (!CallerHoldsDriveKey(odinContext))
        {
            ThrowNoAccess(driveId);
        }
    }

    private static void ThrowNoAccess(Guid driveId) => throw new OdinSecurityException($"No access permitted to drive {driveId}");

    /// <summary>
    /// An encrypted update of a client-locked file (sealed or zkDrive) takes a zero-knowledge drive's own
    /// key header with a new IV, as for any encrypted file: only the owner's devices update one, and they
    /// lock with the drive's key.  The server compares the IVs both key headers show; it can't check that
    /// the file key stayed the same, which is the writer's to keep, or the payloads no longer open.  Callers
    /// refuse a change of encryption first; any other file never gets a client-locked key header
    /// (<see cref="AssertDriveTakes"/>).
    /// </summary>
    public static void AssertFitsUpdate(ServerFileHeader existing, KeyHeader update)
    {
        if (!IsClientLocked(existing))
        {
            return;
        }

        if (update is not ClientLockedKeyHeader { Type: EncryptionType.ZkDrive })
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
