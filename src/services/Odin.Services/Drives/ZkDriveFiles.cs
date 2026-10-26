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
    public static bool IsZkDriveFile(ServerFileHeader header) =>
        header.FileMetadata.IsEncrypted && header.EncryptedKeyHeader?.IsZkDrive() == true;

    /// <summary>
    /// The key header a client sent to write to <paramref name="drive"/>, decided by its type before
    /// anything is opened.  A zero-knowledge drive takes its own key headers, and refuses one locked
    /// with the shared secret unopened: the server would see the file key, and store a key header
    /// nobody could open.  Any other drive refuses a zero-knowledge drive's key header.  A deposit
    /// passes through as today (<see cref="Deposits"/>).
    /// </summary>
    public static KeyHeader OpenForWrite(EncryptedKeyHeader sent, StorageDrive drive, ref SensitiveByteArray sharedSecret)
    {
        if (sent.IsSealed())
        {
            return new SealedKeyHeader(sent);
        }

        if (drive.IsZeroKnowledge)
        {
            if (!sent.IsZkDrive())
            {
                throw new OdinClientException("A zero-knowledge drive takes only key headers locked with its own key",
                    OdinClientErrorCode.DriveIsZeroKnowledge);
            }

            sent.AssertValidZkDrive();
            return new ZkDriveKeyHeader(sent);
        }

        if (sent.IsZkDrive())
        {
            throw new OdinClientException("Only a zero-knowledge drive takes a key header locked with its own key",
                OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
        }

        return sent.DecryptAesToKeyHeader(ref sharedSecret);
    }

    /// <summary>
    /// Whether the caller may read a zero-knowledge drive's own files.  Only the owner console, for now:
    /// it opens the drive's key with the zero-knowledge master key.  An app learns the drive's key only
    /// at consent (docs/zero-knowledge-drives.md, item 4), and an app that can't open the key header
    /// must not be handed one: chat-kmp stalls its whole sync on a file it can't open.
    /// </summary>
    public static bool CallerMayRead(IOdinContext odinContext) => odinContext.Caller.HasMasterKey;

    /// <summary>Refuses the read the way a file is refused to a caller without the drive's key.</summary>
    public static void AssertCallerMayRead(ServerFileHeader header, IOdinContext odinContext)
    {
        if (!CallerMayRead(odinContext))
        {
            throw new OdinSecurityException($"No access permitted to drive {header.FileMetadata.File.DriveId}");
        }
    }

    /// <summary>
    /// An update must change the key header's IV, as for any encrypted file.  The server compares the
    /// IVs both key headers show; it can't check that the file key stayed the same, which is the
    /// writer's to keep, or the payloads no longer open.
    /// </summary>
    public static void AssertIvChanged(EncryptedKeyHeader stored, ZkDriveKeyHeader update)
    {
        if (ByteArrayUtil.EquiByteArrayCompare(update.Iv, stored.Iv))
        {
            throw new OdinClientException("When updating a file, you must change the Iv",
                OdinClientErrorCode.MustRotateKeyHeaderIvWhenUpdating);
        }
    }

    /// <summary>
    /// The new key header an update to <paramref name="existing"/> stores.  A zero-knowledge drive's file
    /// takes only its own key header, with a new IV; any other file takes none of those.
    /// </summary>
    public static void AssertFitsUpdate(ServerFileHeader existing, KeyHeader update)
    {
        if (IsZkDriveFile(existing))
        {
            if (update is not ZkDriveKeyHeader zkUpdate)
            {
                throw new OdinClientException("A zero-knowledge drive's file takes only a key header locked with its own key",
                    OdinClientErrorCode.DriveIsZeroKnowledge);
            }

            AssertIvChanged(existing.EncryptedKeyHeader, zkUpdate);
        }
        else if (update is ZkDriveKeyHeader)
        {
            throw new OdinClientException("Only a zero-knowledge drive's file takes a key header locked with its own key",
                OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive);
        }
    }

    /// <summary>
    /// A zero-knowledge drive's own file isn't sent to anyone: the recipient would need the drive's
    /// key.  Chat sends through deposits (docs/zero-knowledge-drive-api-plan.md, step 2).
    /// </summary>
    public static void AssertNotSent(ServerFileHeader header)
    {
        if (IsZkDriveFile(header))
        {
            throw new OdinClientException("A zero-knowledge drive's file can't be sent", OdinClientErrorCode.ZkDriveFileCannotBeSent);
        }
    }
}
