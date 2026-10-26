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
    /// anything is opened (<see cref="AssertDriveTakes"/>).  A zero-knowledge drive's own key header comes
    /// only from a caller holding its key.  A deposit passes through as today (<see cref="Deposits"/>).
    /// </summary>
    private static KeyHeader OpenForWrite(EncryptedKeyHeader? sent, StorageDrive drive, ref SensitiveByteArray sharedSecret,
        IOdinContext odinContext)
    {
        if (sent == null)
        {
            throw new OdinClientException("An encrypted file needs its key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        AssertDriveTakes(drive, sent.Type);
        if (!sent.IsZkDrive())
        {
            return sent.DecryptOrPassSealed(ref sharedSecret);
        }

        AssertCallerHoldsDriveKey(drive.Id, odinContext);
        sent.AssertValidZkDrive();
        return new ZkDriveKeyHeader(sent);
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
    /// Which drive takes which key header: a zero-knowledge drive refuses one locked with the shared secret
    /// (the server would see the file key, and store a key header nobody could open), and any other drive
    /// refuses a zero-knowledge drive's own.  Deposits (<see cref="EncryptionType.Sealed"/>) are
    /// <see cref="Deposits.AssertCanHold"/>'s.
    /// </summary>
    public static void AssertDriveTakes(StorageDrive drive, EncryptionType type)
    {
        if (type == EncryptionType.Sealed)
        {
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
    /// Whether <paramref name="header"/> may reach the caller: a zero-knowledge drive's own file only
    /// reaches one holding the drive's key, whatever else it holds (a grant from before the drive was
    /// converted still carries the old key), and the system's jobs (a file's TTL).  Says nothing about any
    /// other file.  Every read and write of a stored header asks this, by query, socket or id.
    /// </summary>
    public static bool VisibleTo(ServerFileHeader? header, IOdinContext odinContext) =>
        !IsZkDriveFile(header) || CallerHoldsDriveKey(odinContext) || odinContext.Caller?.IsSystem == true;

    /// <summary>Refuses a file the caller may not see (<see cref="VisibleTo"/>) as one refused for want of the drive's key.</summary>
    public static void AssertVisibleTo(ServerFileHeader header, IOdinContext odinContext)
    {
        if (!VisibleTo(header, odinContext))
        {
            ThrowNoAccess(header.FileMetadata.File.DriveId);
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
    /// An encrypted update of a zero-knowledge drive's file takes its own key header with a new IV, as for
    /// any encrypted file.  The server compares the IVs both key headers show; it can't check that the file
    /// key stayed the same, which is the writer's to keep, or the payloads no longer open.  Callers refuse a
    /// change of encryption first; any other file never gets a <see cref="ZkDriveKeyHeader"/>
    /// (<see cref="AssertDriveTakes"/>).
    /// </summary>
    public static void AssertFitsUpdate(ServerFileHeader existing, KeyHeader update)
    {
        if (!IsZkDriveFile(existing))
        {
            return;
        }

        if (update is not ZkDriveKeyHeader)
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
