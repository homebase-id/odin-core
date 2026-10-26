using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives;

/// <summary>
/// A deposit is a file someone writes to a drive they can't read: they encrypt it with a key header
/// of their own and seal that key header to the drive's write-only public key
/// (<see cref="EncryptedKeyHeader.Sealed"/>).  The server stores the sealed key header as received,
/// passes it on as is and returns it as stored; it never opens or re-locks it
/// (docs/zero-knowledge-recovery-plan.md, "Deposits").
/// </summary>
/// <remarks>
/// For now a deposit is a header only: a payload's key header leaves through a fixed-size response
/// header (<c>SharedSecretEncryptedHeader64</c>: IV, AES key, version) that a sealed one doesn't fit.
/// </remarks>
public static class Deposits
{
    public static bool IsDeposit(bool isEncrypted, EncryptedKeyHeader keyHeader) => isEncrypted && keyHeader?.IsSealed() == true;

    public static bool IsDeposit(ServerFileHeader header) => IsDeposit(header.FileMetadata.IsEncrypted, header.EncryptedKeyHeader);

    /// <summary>
    /// A stored file's key header, opened with the drive key the caller holds; a deposit's comes back
    /// sealed, as stored, and needs no drive key.  Any other file needs the caller to hold the drive
    /// key, even an unencrypted one (sending a file always has).  Re-lock it with
    /// <see cref="EncryptedKeyHeader.EncryptKeyHeaderAes"/>, which passes a sealed one on as it is.
    /// </summary>
    public static KeyHeader OpenWithDriveKey(ServerFileHeader header, IOdinContext odinContext)
    {
        if (IsDeposit(header))
        {
            return new SealedKeyHeader(header.EncryptedKeyHeader);
        }

        var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
        var keyHeader = header.FileMetadata.IsEncrypted
            ? header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey)
            : KeyHeader.Empty();
        storageKey.Wipe();
        return keyHeader;
    }

    /// <summary>Refuses a malformed deposit, and one that carries payloads.</summary>
    public static void AssertValid(FileMetadata metadata, EncryptedKeyHeader sealedKeyHeader)
    {
        sealedKeyHeader.AssertValidSealed();
        if (metadata.Payloads?.Any() == true)
        {
            throw new OdinClientException("A deposit carries no payloads", OdinClientErrorCode.InvalidPayload);
        }
    }

    /// <summary>Refuses a deposit for a drive that can't hold one (<see cref="StorageDrive.CanHoldDeposits"/>).</summary>
    public static void AssertCanHold(StorageDrive drive)
    {
        if (!drive.CanHoldDeposits)
        {
            throw new OdinClientException("Only a zero-knowledge drive takes a deposit (a sealed key header)",
                OdinClientErrorCode.DepositNeedsZeroKnowledgeDrive);
        }
    }

    /// <summary>
    /// A deposit is changed only by writing it again: nobody here can open its key to check an update
    /// against it or re-lock it.
    /// </summary>
    public static void AssertNotDeposit(ServerFileHeader existingHeader) =>
        AssertNotDeposit(existingHeader.FileMetadata.IsEncrypted, existingHeader.EncryptedKeyHeader);

    public static void AssertNotDeposit(bool isEncrypted, EncryptedKeyHeader keyHeader)
    {
        if (IsDeposit(isEncrypted, keyHeader))
        {
            throw new OdinClientException("A deposit can't be updated; write it again", OdinClientErrorCode.DepositCannotBeUpdated);
        }
    }
}
