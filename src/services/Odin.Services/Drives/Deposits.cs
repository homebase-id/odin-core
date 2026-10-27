using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives;

/// <summary>
/// A deposit is a file someone writes to a drive they can't read: they encrypt it with a key header
/// of their own and seal that key header to the drive's write-only public key
/// (<see cref="EncryptedKeyHeader.Sealed"/>).  The server stores the sealed key header as received,
/// passes it on as is and returns it as stored; it never opens or re-locks it
/// (docs/zero-knowledge-recovery-plan.md, "Deposits").  On the drive, a deposit is one of the two kinds
/// of client-locked file, and who may see or change it is <see cref="ZkDriveFiles"/>'s
/// (docs/zero-knowledge-drive-api-plan.md, step 2).
/// </summary>
/// <remarks>
/// For now a deposit is a header only.  It was first because a payload's key header left through a
/// fixed-size response header (<c>SharedSecretEncryptedHeader64</c>) a sealed one doesn't fit; step 1
/// dropped that header for every client-locked file, so what stops a deposit with payloads now is only
/// that nothing needs one yet.  Chat lifts it, here (docs/zero-knowledge-drive-api-plan.md, step 2).
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
            return new ClientLockedKeyHeader(header.EncryptedKeyHeader);
        }

        if (!header.FileMetadata.IsEncrypted)
        {
            // Sending a file has always needed the drive key, even an unencrypted one
            odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId).Wipe();
            return KeyHeader.Empty();
        }

        return DriveFileUtility.OpenWithStorageKey(header, odinContext);
    }

    /// <summary>Refuses a malformed deposit, and one that carries payloads.</summary>
    public static void AssertValid(FileMetadata metadata, EncryptedKeyHeader sealedKeyHeader)
    {
        sealedKeyHeader.AssertValidSealed();
        if (metadata.Payloads?.Any() == true)
        {
            ThrowCarriesNoPayloads();
        }
    }

    /// <summary>Refuses payloads for a stored deposit, as <see cref="AssertValid"/> does for a new one.</summary>
    public static void AssertTakesNoPayloads(ServerFileHeader existingHeader)
    {
        if (IsDeposit(existingHeader))
        {
            ThrowCarriesNoPayloads();
        }
    }

    internal static void ThrowCarriesNoPayloads() =>
        throw new OdinClientException("A deposit carries no payloads", OdinClientErrorCode.InvalidPayload);

    /// <summary>
    /// Refuses a deposit for a drive that can't hold one (<see cref="StorageDrive.CanHoldDeposits"/>); asked
    /// through <see cref="ZkDriveFiles.AssertDriveTakes"/>, which decides which drive takes which key header.
    /// </summary>
    internal static void AssertCanHold(StorageDrive drive)
    {
        if (!drive.CanHoldDeposits)
        {
            throw new OdinClientException("Only a zero-knowledge drive takes a deposit (a sealed key header)",
                OdinClientErrorCode.DepositNeedsZeroKnowledgeDrive);
        }
    }
}
