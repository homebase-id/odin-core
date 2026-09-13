using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Encryption;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// Writes the tenant's OpenPGP keyring onto the Email app's drive, server-side, on the caller's
/// behalf — the same thing <see cref="Odin.Services.Profile.ProfileAttributeService"/> does for
/// profile attributes.
///
/// Why the server writes it rather than returning it for the client to store: the keyring must be
/// durable BEFORE its certificate is published. If the client stored it, an app killed between
/// "server generated and returned the key" and "client finished writing it" would leave the
/// identity with a published certificate whose private half nobody holds — mail arriving,
/// encrypted, permanently unreadable. Writing here makes "a certificate is published" imply "the
/// keyring exists", by construction.
///
/// The keyring is encrypted at rest like any drive file, under a key header the caller's own
/// storage key protects. Owner-only ACL, never distributed.
/// </summary>
public class EmailKeyMaterialWriter(
    ILogger<EmailKeyMaterialWriter> logger,
    StandardFileSystem fileSystem)
{
    // The client will not be allowed to rewrite anything larger than its own 7000-byte header
    // budget, so hold to the tighter of the two limits rather than the server's 10KB.
    private const int MaxHeaderContentBytes = 7000;

    private static Guid DriveId => WellKnownAppDrives.EmailAppDrive.Alias;

    /// <summary>
    /// Appends a keyring as a new file and returns its unique id. Never overwrites: an older key
    /// is what makes older mail readable.
    /// </summary>
    public async Task<Guid> WriteKeyMaterialAsync(
        OpenPgpKeyMaterial material,
        string userId,
        IOdinContext odinContext)
    {
        AssertCanResolveStorageKey(odinContext);

        var uniqueId = Guid.NewGuid();
        var content = new EmailKeyMaterialContent
        {
            SecretKeyArmored = material.SecretKeyArmored,
            PublicCertificateArmored = material.PublicCertificateArmored,
            FingerprintHex = material.FingerprintHex,
            UserId = userId,
            CreatedUtc = UnixTimeUtc.Now().milliseconds,
        };

        await WriteFileAsync(EmailDriveFileTypes.KeyMaterial, uniqueId, content, odinContext);

        logger.LogInformation("Wrote email key material {fingerprint} to the email drive", material.FingerprintHex);
        return uniqueId;
    }

    /// <summary>
    /// Points the singleton at <paramref name="keyFileUniqueId"/>, creating it on first use and
    /// overwriting it on rotation. Server-only-written, so there is no cross-writer race and no
    /// conflict loop to run.
    /// </summary>
    public async Task UpdateCurrentKeyPointerAsync(
        Guid keyFileUniqueId,
        string fingerprintHex,
        IOdinContext odinContext)
    {
        AssertCanResolveStorageKey(odinContext);

        var content = new EmailCurrentKeyContent
        {
            KeyFileUniqueId = keyFileUniqueId,
            FingerprintHex = fingerprintHex,
            UpdatedUtc = UnixTimeUtc.Now().milliseconds,
        };

        await WriteFileAsync(
            EmailDriveFileTypes.CurrentKeyPointer,
            EmailDriveFileTypes.CurrentKeyPointerUniqueId,
            content,
            odinContext);
    }

    //

    private async Task WriteFileAsync<T>(int fileType, Guid uniqueId, T content, IOdinContext odinContext)
    {
        var existing = await FindByUniqueIdAsync(uniqueId, odinContext);

        var file = existing?.FileMetadata.File
                   ?? await fileSystem.Storage.CreateInternalFileId(DriveId, odinContext);

        var keyHeader = KeyHeader.NewRandom16();
        var json = OdinSystemSerializer.Serialize(content);
        var storedContent = keyHeader.EncryptDataAes(json.ToUtf8ByteArray()).ToBase64();

        if (storedContent.ToUtf8ByteArray().Length > MaxHeaderContentBytes)
        {
            // A P-384 keyring is ~2KB encrypted, so this is a guard against a future shape
            // change rather than an expected condition.
            throw new OdinSystemException("Email key material does not fit in a file header");
        }

        var metadata = new FileMetadata(file)
        {
            AppData = new AppFileMetaData
            {
                FileType = fileType,
                UniqueId = uniqueId,
                Content = storedContent,
            },
            IsEncrypted = true,
            // Required for the update path: UpdateActiveFileHeader refuses a header that does not
            // declare itself active.
            FileState = FileState.Active,
            VersionTag = existing?.FileMetadata.VersionTag,
        };

        var serverMetadata = new ServerMetadata
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = false,
        };

        var header = await fileSystem.Storage.CreateServerFileHeader(
            file, keyHeader, metadata, serverMetadata, odinContext);

        if (existing == null)
        {
            await fileSystem.Storage.WriteNewFileHeader(file, header, odinContext, raiseEvent: true);
        }
        else
        {
            await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
        }
    }

    private async Task<ServerFileHeader?> FindByUniqueIdAsync(Guid uniqueId, IOdinContext odinContext)
    {
        var results = await fileSystem.Query.GetBatch(
            DriveId,
            new FileQueryParams
            {
                ClientUniqueIdAtLeastOne = [uniqueId],
            },
            new QueryBatchResultOptions { MaxRecords = 1 },
            odinContext);

        var match = results.SearchResults.FirstOrDefault();
        if (match == null)
        {
            return null;
        }

        return await fileSystem.Storage.GetServerFileHeader(
            new InternalDriveFileId { DriveId = DriveId, FileId = match.FileId }, odinContext);
    }

    /// <summary>
    /// A caller without a storage key for this drive cannot have its file encrypted, and the
    /// failure would otherwise surface deep inside key resolution.
    /// </summary>
    private static void AssertCanResolveStorageKey(IOdinContext odinContext)
    {
        if (!odinContext.PermissionsContext.TryGetDriveStorageKey(DriveId, out _))
        {
            throw new OdinSystemException(
                "Email drive access granted without a storage key -- cannot encrypt the keyring.");
        }
    }
}
