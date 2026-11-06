using System;
using System.Linq;
using Serilog;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives;

/// <summary>
/// A deposit is a file someone writes to a drive they can't read: they encrypt it with a key header of their own and
/// seal that key header to the drive's write-only public key (<see cref="EncryptedKeyHeader.Sealed"/>).  One set of rules
/// on every drive that takes one (docs/server-share-sealed-arrivals-plan.md): it carries payloads, is never a comment,
/// and is stored as received.  On a zero-knowledge drive the server never opens it, and who may see or change it is
/// <see cref="ZkDriveFiles"/>'s; on a Server Share drive the server opens it on a read and locks it as its own
/// (<see cref="ConvertedOnServerShare"/>).
/// </summary>
public static class Deposits
{
    public static bool IsDeposit(bool isEncrypted, EncryptedKeyHeader keyHeader) => isEncrypted && keyHeader?.IsSealed() == true;

    public static bool IsDeposit(ServerFileHeader header) => IsDeposit(header.FileMetadata.IsEncrypted, header.EncryptedKeyHeader);

    /// <summary>
    /// A stored file's key header, opened with the drive key the caller holds; a deposit's comes back
    /// sealed, as stored, and needs no drive key.  Any other file needs the caller to hold the drive
    /// key, even an unencrypted one (sending a file always has).  So does a member's comment on its file's key
    /// (<see cref="EncryptionType.FileKey"/>), staged on the transient drive on its way to a Selective Share drive.
    /// Re-lock it with <see cref="EncryptedKeyHeader.EncryptKeyHeaderAes"/>, which passes either on as it is.
    /// </summary>
    public static KeyHeader OpenWithDriveKey(ServerFileHeader header, IOdinContext odinContext)
    {
        // A deposit, or a member's comment on its file's key: stored and sent on as it came.  A share key header, which
        // only the transient drive stages, passes on too, and the recipient refuses it (ZkDriveFiles.AssertPeerMayWrite)
        if (header.FileMetadata.IsEncrypted && header.EncryptedKeyHeader?.IsPassedOnAsSent() == true)
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
    /// Where a deposit may carry payloads: on every drive that takes one, as a file has its content, under one set of
    /// deposit rules for every drive (docs/server-share-sealed-arrivals-plan.md, "Decisions").
    /// </summary>
    public static bool MayCarryPayloads(StorageDrive drive) => drive.CanHoldDeposits;

    /// <summary>Refuses payloads added with a sealed key header, as <see cref="AssertValid"/> does for a new deposit.</summary>
    public static void AssertTakesNoPayloads(KeyHeader keyHeader, bool addsPayloads, StorageDrive drive)
    {
        if (addsPayloads && keyHeader is ClientLockedKeyHeader { IsSealed: true } && !MayCarryPayloads(drive))
        {
            ThrowCarriesNoPayloads();
        }
    }

    /// <summary>Refuses payloads for a stored deposit, as <see cref="AssertValid"/> does for a new one.</summary>
    public static void AssertTakesNoPayloads(ServerFileHeader existingHeader, StorageDrive drive)
    {
        if (IsDeposit(existingHeader) && !MayCarryPayloads(drive))
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
    internal static void AssertCanHold(StorageDrive drive, FileSystemType fileSystem)
    {
        // A deposit is a file: a comment's key comes with the drive's model, never sealed
        if (fileSystem == FileSystemType.Comment)
        {
            throw new OdinClientException("A comment is never sealed", OdinClientErrorCode.InvalidKeyHeader);
        }

        if (!drive.CanHoldDeposits)
        {
            throw new OdinClientException("This drive takes no deposit (a sealed key header): only a zero-knowledge drive, or a " +
                                          "Server Share drive that isn't a community channel, does", OdinClientErrorCode.DepositNeedsZeroKnowledgeDrive);
        }
    }

    /// <summary>
    /// On a Server Share drive, a deposit's key header as the server's own: the seal opened with the drive's write-only
    /// private key, which the drive key the caller's request carries opens, and the file key locked with the drive key under
    /// the same IV, as any file there.  Null when it isn't one the server converts for this caller: not a deposit, not a
    /// Server Share drive, sealed to another key, or no drive key in the request.  The private key is opened for this call
    /// only (<see cref="Odin.Core.Cryptography.Data.EccFullKeyData.OpenPrivateKey"/>).
    /// </summary>
    public static EncryptedKeyHeader ConvertedOnServerShare(StorageDrive drive, ServerFileHeader header, IOdinContext odinContext)
    {
        if (drive?.ServerOpensDeposits != true || header == null || !IsDeposit(header) ||
            !drive.IsSealedToWriteOnlyKey(header.EncryptedKeyHeader.EncryptedAesKey) ||
            !odinContext.PermissionsContext.TryGetDriveStorageKey(drive.Id, out var storageKey))
        {
            return null;
        }

        byte[] plain = null;
        byte[] privateScalar = null;
        try
        {
            var privateKey = drive.WriteOnlyKeyPair.OpenPrivateKey(storageKey);
            privateScalar = privateKey.D;
            plain = ZkSeal.Open(header.EncryptedKeyHeader.EncryptedAesKey, privateKey);

            // Exactly the file's IV and key, under the IV the header shows: anything else would lock a key that isn't the
            // file's over its only sealed copy
            if (plain.Length != 32 || !plain.AsSpan(0, 16).SequenceEqual(header.EncryptedKeyHeader.Iv))
            {
                Log.Warning("A deposit on drive {driveId} opened to something other than its file key; left sealed", drive.Id);
                return null;
            }

            var opened = KeyHeader.FromCombinedBytes(plain);
            try
            {
                return EncryptedKeyHeader.EncryptKeyHeaderAes(opened, opened.Iv, ref storageKey);
            }
            finally
            {
                opened.AesKey.Wipe();
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Sealed to this drive's key but not openable with it, by any error: left sealed, never a failed read
            Log.Warning(e, "A deposit on drive {driveId} could not be opened; left sealed", drive.Id);
            return null;
        }
        finally
        {
            if (plain != null)
            {
                Array.Clear(plain);
            }

            if (privateScalar != null)
            {
                Array.Clear(privateScalar);
            }

            storageKey.Wipe();
        }
    }
}
