using System.Linq;
using Odin.Core.Exceptions;
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
    public static void AssertNotDeposit(ServerFileHeader existingHeader)
    {
        if (IsDeposit(existingHeader))
        {
            throw new OdinClientException("A deposit can't be updated; write it again", OdinClientErrorCode.DepositCannotBeUpdated);
        }
    }
}
