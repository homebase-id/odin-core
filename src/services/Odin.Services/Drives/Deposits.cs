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
    /// A deposit, or a member's comment on a Selective Share drive encrypted with its file's key (section 10): the key
    /// header is stored and sent on as it came, so neither needs the drive's key.
    /// </summary>
    public static bool IsStoredAsSent(bool isEncrypted, EncryptedKeyHeader keyHeader) =>
        IsDeposit(isEncrypted, keyHeader) || (isEncrypted && keyHeader?.IsFileKey() == true);

    /// <summary>
    /// A stored file's key header, opened with the drive key the caller holds; a deposit's comes back
    /// sealed, as stored, and needs no drive key.  Any other file needs the caller to hold the drive
    /// key, even an unencrypted one (sending a file always has).  So does a member's comment on its file's key
    /// (<see cref="EncryptionType.FileKey"/>), staged on the transient drive on its way to a Selective Share drive.
    /// Re-lock it with <see cref="EncryptedKeyHeader.EncryptKeyHeaderAes"/>, which passes either on as it is.
    /// </summary>
    public static KeyHeader OpenWithDriveKey(ServerFileHeader header, IOdinContext odinContext)
    {
        if (IsStoredAsSent(header.FileMetadata.IsEncrypted, header.EncryptedKeyHeader))
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

    /// <summary>Refuses a malformed deposit, and one with payloads for a drive whose deposits carry none.</summary>
    public static void AssertValid(FileMetadata metadata, EncryptedKeyHeader sealedKeyHeader, StorageDrive drive)
    {
        sealedKeyHeader.AssertValidSealed();
        if (metadata.Payloads?.Any() == true && !MayCarryPayloads(drive))
        {
            ThrowCarriesNoPayloads();
        }
    }

    /// <summary>
    /// Where a deposit may carry payloads: a Selective Share drive, where a member adds a file with its content to a
    /// shared folder (docs/selective-share-pr3-plan.md, section 7), and the transient drive a deposit is sent from,
    /// where the receiving drive decides.  Anywhere else a deposit is still a header only.
    /// </summary>
    public static bool MayCarryPayloads(StorageDrive drive) =>
        drive.IsSelectiveShare || SystemDriveConstants.IsTransientTempDrive(drive.Id);

    /// <summary>Refuses payloads added with a sealed key header, as <see cref="AssertValid"/> does for a new deposit.</summary>
    public static void AssertTakesNoPayloads(KeyHeader keyHeader, bool addsPayloads, StorageDrive drive)
    {
        if (addsPayloads && keyHeader is ClientLockedKeyHeader { Type: EncryptionType.Sealed } && !MayCarryPayloads(drive))
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

    private static void ThrowCarriesNoPayloads() =>
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
