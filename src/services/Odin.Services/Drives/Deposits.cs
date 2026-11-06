using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
    /// On a Server Share drive, the deposits among <paramref name="headers"/> whose key header the server makes its own for
    /// this caller, each with that key header: the seal opened with the drive's write-only private key, which the drive key
    /// the caller's request carries opens, and the file key locked with the drive key under the same IV, as any file there.
    /// Empty when the drive isn't a Server Share drive the server opens deposits on or the request carries no drive key;
    /// a header that isn't a deposit, is sealed to another key or doesn't open is left out.  The drive key and the private
    /// key are opened once for the batch, and for this call only
    /// (<see cref="Odin.Core.Cryptography.Data.EccFullKeyData.OpenPrivateKey"/>).
    /// </summary>
    public static List<(ServerFileHeader Header, EncryptedKeyHeader KeyHeader)> ConvertedOnServerShare(StorageDrive drive,
        IEnumerable<ServerFileHeader> headers, IOdinContext odinContext)
    {
        if (drive?.ServerOpensDeposits != true)
        {
            return [];
        }

        var deposits = headers.Where(h => IsDeposit(h) && drive.IsSealedToWriteOnlyKey(h.EncryptedKeyHeader.EncryptedAesKey))
            .ToList();
        if (deposits.Count == 0 || !odinContext.PermissionsContext.TryGetDriveStorageKey(drive.Id, out var storageKey))
        {
            return [];
        }

        var privateKey = default(ECParameters);
        try
        {
            privateKey = drive.WriteOnlyKeyPair.OpenPrivateKey(storageKey);
            using var me = ECDiffieHellman.Create(privateKey);
            var converted = new List<(ServerFileHeader, EncryptedKeyHeader)>();
            foreach (var header in deposits)
            {
                if (Converted(drive, header, me, ref storageKey) is { } keyHeader)
                {
                    converted.Add((header, keyHeader));
                }
            }

            return converted;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Warning(e, "The write-only private key of drive {driveId} could not be opened; its deposits left sealed", drive.Id);
            return [];
        }
        finally
        {
            if (privateKey.D != null)
            {
                Array.Clear(privateKey.D);
            }

            storageKey.Wipe();
        }
    }

    private static EncryptedKeyHeader Converted(StorageDrive drive, ServerFileHeader header, ECDiffieHellman me,
        ref SensitiveByteArray storageKey)
    {
        byte[] plain = null;
        KeyHeader opened = null;
        try
        {
            plain = ZkSeal.Open(header.EncryptedKeyHeader.EncryptedAesKey, me);

            // Exactly the file's IV and key, under the IV the header shows: anything else would lock a key that isn't the
            // file's over its only sealed copy
            if (plain.Length != 32 || !plain.AsSpan(0, 16).SequenceEqual(header.EncryptedKeyHeader.Iv))
            {
                Log.Warning("A deposit on drive {driveId} opened to something other than its file key; left sealed", drive.Id);
                return null;
            }

            opened = KeyHeader.FromCombinedBytes(plain);
            return EncryptedKeyHeader.EncryptKeyHeaderAes(opened, opened.Iv, ref storageKey);
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

            opened?.AesKey.Wipe();
        }
    }
}
