#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives;

/// <summary>
/// A zero-knowledge drive's own files: an owner device locked each key header with the drive's key
/// (<see cref="EncryptionType.ZkDrive"/>), which the server doesn't hold.  The server stores that key
/// header as received and returns it as stored; it never opens or re-locks it
/// (docs/zero-knowledge-drive-api-plan.md, step 1).
/// </summary>
public static class ZkDriveFiles
{
    public static bool IsZkDriveFile(ServerFileHeader? header) =>
        header?.FileMetadata?.IsEncrypted == true && header.EncryptedKeyHeader?.IsZkDrive() == true;

    /// <summary>
    /// The key header a client sent to write to <paramref name="drive"/>, decided by its type before
    /// anything is opened.  A zero-knowledge drive takes its own key headers, only from a caller holding
    /// its key, and refuses one locked with the shared secret unopened: the server would see the file
    /// key, and store a key header nobody could open.  Any other drive refuses a zero-knowledge drive's
    /// key header.  A deposit passes through as today (<see cref="Deposits"/>).
    /// </summary>
    public static KeyHeader OpenForWrite(EncryptedKeyHeader sent, StorageDrive drive, ref SensitiveByteArray sharedSecret,
        IOdinContext odinContext)
    {
        if (sent.IsSealed())
        {
            return new SealedKeyHeader(sent);
        }

        if (!drive.IsZeroKnowledge)
        {
            if (sent.IsZkDrive())
            {
                ThrowNeedsZeroKnowledgeDrive();
            }

            return sent.DecryptAesToKeyHeader(ref sharedSecret);
        }

        if (!sent.IsZkDrive())
        {
            throw new OdinClientException("A zero-knowledge drive takes only key headers locked with its own key",
                OdinClientErrorCode.DriveIsZeroKnowledge);
        }

        AssertCallerHoldsDriveKey(drive.Id, odinContext);
        sent.AssertValidZkDrive();
        return new ZkDriveKeyHeader(sent);
    }

    /// <summary>Only a zero-knowledge drive stores a <see cref="ZkDriveKeyHeader"/>.</summary>
    public static void AssertCanHold(StorageDrive drive)
    {
        if (!drive.IsZeroKnowledge)
        {
            ThrowNeedsZeroKnowledgeDrive();
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
    /// Whether <paramref name="header"/> may reach the caller: a zero-knowledge drive's own file only
    /// reaches one holding the drive's key, whatever else it holds (a grant from before the drive was
    /// converted still carries the old key).  Says nothing about any other file.
    /// </summary>
    public static bool VisibleTo(ServerFileHeader? header, IOdinContext odinContext) =>
        !IsZkDriveFile(header) || CallerHoldsDriveKey(odinContext);

    /// <summary>Refuses the caller the way a file is refused to a caller without the drive's key.</summary>
    public static void AssertCallerHoldsDriveKey(Guid driveId, IOdinContext odinContext)
    {
        if (!CallerHoldsDriveKey(odinContext))
        {
            throw new OdinSecurityException($"No access permitted to drive {driveId}");
        }
    }

    /// <summary>
    /// An encrypted update of a zero-knowledge drive's file takes its own key header with a new IV, as for
    /// any encrypted file.  The server compares the IVs both key headers show; it can't check that the file
    /// key stayed the same, which is the writer's to keep, or the payloads no longer open.  Any other file
    /// never gets a <see cref="ZkDriveKeyHeader"/>: <see cref="OpenForWrite"/> refuses one for its drive.
    /// </summary>
    public static void AssertFitsUpdate(ServerFileHeader existing, KeyHeader update)
    {
        if (!IsZkDriveFile(existing))
        {
            return;
        }

        if (update is not ZkDriveKeyHeader)
        {
            throw new OdinClientException("A zero-knowledge drive's file takes only a key header locked with its own key",
                OdinClientErrorCode.DriveIsZeroKnowledge);
        }

        if (ByteArrayUtil.EquiByteArrayCompare(update.Iv, existing.EncryptedKeyHeader.Iv))
        {
            throw new OdinClientException("When updating a file, you must change the Iv",
                OdinClientErrorCode.MustRotateKeyHeaderIvWhenUpdating);
        }
    }

    /// <summary>
    /// A zero-knowledge drive's own file isn't sent to anyone: the recipient would need the drive's key.
    /// Writes refuse recipients before they store anything; Chat will send through deposits
    /// (docs/zero-knowledge-drive-api-plan.md, step 2).
    /// </summary>
    public static void AssertNotSent<T>(KeyHeader keyHeader, IEnumerable<T>? recipients)
    {
        if (keyHeader is ZkDriveKeyHeader && recipients?.Any() == true)
        {
            ThrowCannotBeSent();
        }
    }

    /// <summary>The same, for a stored file about to be sent.</summary>
    public static void AssertNotSent(ServerFileHeader header)
    {
        if (IsZkDriveFile(header))
        {
            ThrowCannotBeSent();
        }
    }

    private static void ThrowCannotBeSent() =>
        throw new OdinClientException("A zero-knowledge drive's file can't be sent", OdinClientErrorCode.ZkDriveFileCannotBeSent);

    private static void ThrowNeedsZeroKnowledgeDrive() =>
        throw new OdinClientException("Only a zero-knowledge drive takes a key header locked with its own key",
            OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
}
