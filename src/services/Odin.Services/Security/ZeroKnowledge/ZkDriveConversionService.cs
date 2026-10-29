#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// Converts a drive to zero knowledge while it keeps its key (docs/zero-knowledge-drive-api-plan.md, step 3): the owner's
/// browser re-locks the drive key with the zero-knowledge master key, and the server drops every copy it could open.
/// No file is re-encrypted.  Only the drives in <see cref="ConvertibleDrives"/>, for now.
/// </summary>
/// <remarks>
/// The trade, accepted: the server opened the key every time it served the drive before, so a key leaked then still
/// opens the drive's files.  Conversion gives "the server can't open it from now on" (zero-knowledge-drives.md, "Risk we
/// accept").
/// </remarks>
public class ZkDriveConversionService(
    ILogger<ZkDriveConversionService> logger,
    DriveManager driveManager,
    IAppRegistrationService appRegistrationService,
    CircleNetworkService circleNetworkService,
    CircleNetworkRequestService circleNetworkRequestService,
    TableDriveMainIndexCached driveMainIndex,
    OdinContextCache contextCache,
    AppNotificationDispatcher notificationDispatcher,
    IdentityDatabase db)
{
    /// <summary>
    /// The key of a drive about to be converted, for the browser to re-lock: the server's last look at it.  The owner's
    /// context holds it, opened with the master key, and the response travels under the owner's shared secret.
    /// </summary>
    public async Task<string> GetDriveKeyForConversionAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await GetConvertibleDriveAsync(driveId);

        var key = odinContext.PermissionsContext.GetDriveStorageKey(driveId);
        return key.GetKey().ToBase64();
    }

    /// <summary>
    /// Converts the drive in one transaction: the drive row (the browser's copy of the key, no master-key copy, a new
    /// write-only key pair), each granted app's copy of the key locked with its zero-knowledge key, no key left on any
    /// grant, and every encrypted file's key header relabelled from <c>aes</c> to <c>zkDrive</c>.
    /// </summary>
    public async Task ConvertDriveKeepingKeyAsync(ZkConvertDriveKeepingKeyRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();

        var writeOnlyPublicKey = DriveWriteOnlyKey.FromPublicKeyJwk(request.WriteOnlyPublicKeyJwk);

        int relabelled;
        StorageDrive drive;
        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            // In the transaction, which reads past the drive cache: a second conversion at once finds it converted
            drive = await GetConvertibleDriveAsync(request.DriveId);
            AssertLockedWithTheDriveKey(request, odinContext.PermissionsContext.GetDriveStorageKey(drive.Id));

            await driveManager.ApplyZeroKnowledgeKeysKeepingKeyAsync(drive.Id, request.SharingModel,
                request.ZkMasterKeyEncryptedStorageKey, writeOnlyPublicKey, request.ZkStorageKeyEncryptedWriteOnlyPrivateKey,
                odinContext);

            await appRegistrationService.SetZkAppKeysForConversionAsync(drive.Id, request.Apps, odinContext);
            await circleNetworkService.RemoveStorageKeyFromAllGrantsAsync(drive.Id, odinContext);
            await circleNetworkRequestService.RemoveStorageKeyFromSentRequestsAsync(drive.Id, odinContext);

            relabelled = await RelabelKeyHeadersAsync(drive.Id);

            // Cached contexts carry grants with the key, and the apps' contexts no copies yet; so does every open
            // socket's, kept from when it authenticated
            tx.AddPostCommitAction(() => contextCache.ResetAsync());
            tx.AddPostCommitAction(() => notificationDispatcher.PublishDriveKeysChangedAsync(drive.Id));
            tx.Commit();
        }

        logger.LogInformation("Drive {driveId} is now zero knowledge and kept its key; {count} key headers relabelled zkDrive",
            drive.Id, relabelled);
    }

    /// <summary>
    /// Every file whose key header the server locked with this drive's key becomes <c>zkDrive</c>: version 1 of
    /// <c>zkDrive</c> is the same 48 bytes as <c>aes</c> under the drive key (EncryptedKeyHeader), so only the type changes
    /// and nothing is opened.  The version tag and <c>modified</c> don't move, so no client resyncs.  A header is
    /// relabelled when it has the shape every <c>zkDrive</c> header must (<see cref="EncryptedKeyHeader.HasZkDriveShape"/>):
    /// an unencrypted file's empty key header (a zero IV) stays <c>aes</c>; a deleted file's tombstone keeps its real key
    /// header, and is relabelled with the rest.  A file written meanwhile fails the conversion, which the owner retries.
    /// </summary>
    private async Task<int> RelabelKeyHeadersAsync(Guid driveId)
    {
        var relabelled = new List<(Guid, Guid, string)>();
        foreach (var (fileId, versionTag, encryptedKeyHeader) in await driveMainIndex.GetKeyHeadersByDriveIdAsync(driveId))
        {
            var keyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(encryptedKeyHeader);
            if (keyHeader is not { Type: EncryptionType.Aes } || !keyHeader.HasZkDriveShape())
            {
                continue;
            }

            keyHeader.Type = EncryptionType.ZkDrive;
            relabelled.Add((fileId, versionTag, OdinSystemSerializer.Serialize(keyHeader)));
        }

        var moved = await driveMainIndex.UpdateEncryptedKeyHeadersAsync(driveId, relabelled);
        if (moved.Count > 0)
        {
            throw new OdinClientException($"{moved.Count} file(s) on drive {driveId} changed during the conversion; try again",
                OdinClientErrorCode.VersionTagMismatch);
        }

        return relabelled.Count;
    }

    /// <summary>
    /// The server's last check before a conversion with no way back: the browser locked the new write-only private key
    /// with the drive key it was handed, which the server still holds, and it is the private half of the new public
    /// key.  A browser that locked another key would leave the drive unreadable.  The copies locked with the
    /// zero-knowledge master key and the apps' keys can't be checked: the server opens neither.
    /// </summary>
    private static void AssertLockedWithTheDriveKey(ZkConvertDriveKeepingKeyRequest request, SensitiveByteArray driveKey)
    {
        byte[]? privateKeyPkcs8 = null;
        try
        {
            var locked = request.ZkStorageKeyEncryptedWriteOnlyPrivateKey.FromBase64();
            privateKeyPkcs8 = Odin.Core.Cryptography.Crypto.AesGcm.Decrypt(locked[12..], driveKey, locked[..12]);
            using var ecdh = ECDiffieHellman.Create();
            ecdh.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
            var point = ecdh.ExportParameters(false).Q;
            if (ByteArrayUtil.Combine([0x04], point.X!, point.Y!).SequenceEqual(ZkSeal.RawPublicKey(request.WriteOnlyPublicKeyJwk)))
            {
                return;
            }
        }
        catch (CryptographicException)
        {
            // falls through to the refusal
        }
        finally
        {
            // The server holds the drive key until this conversion commits, but no copy of the write-only key
            if (privateKeyPkcs8 != null)
            {
                CryptographicOperations.ZeroMemory(privateKeyPkcs8);
            }
        }

        throw new OdinClientException("The new write-only key isn't locked with the drive's key", OdinClientErrorCode.ArgumentError);
    }

    /// <summary>
    /// The drives that existed before they could be made zero knowledge and are upgraded this way, for now (plan 3.7).
    /// A new drive is made zero knowledge when it is created, never converted.
    /// </summary>
    private static readonly HashSet<Guid> ConvertibleDrives =
    [
        WellKnownAppDrives.VaultDrive.Alias.Value,
        WellKnownAppDrives.FilesyncDrive.Alias.Value
    ];

    /// <summary>A drive of <see cref="ConvertibleDrives"/>, not yet zero knowledge.</summary>
    private async Task<StorageDrive> GetConvertibleDriveAsync(Guid driveId)
    {
        var drive = (await driveManager.GetDriveAsync(driveId, failIfInvalid: true))!; // never null: it throws instead

        if (drive.IsZeroKnowledge)
        {
            throw new OdinClientException($"Drive {driveId} is already zero knowledge", OdinClientErrorCode.DriveCannotBeConverted);
        }

        if (!ConvertibleDrives.Contains(driveId))
        {
            throw new OdinClientException($"Drive {driveId} isn't one that converts to zero knowledge",
                OdinClientErrorCode.DriveCannotBeConverted);
        }

        return drive;
    }
}

/// <summary>What the browser made to convert a drive that keeps its key (docs/zero-knowledge-drive-api-plan.md, 3.3).</summary>
public sealed class ZkConvertDriveKeepingKeyRequest
{
    public Guid DriveId { get; set; }

    /// <summary>What the drive becomes, as the app that asked for the upgrade wants it: Private or SelectiveShare.</summary>
    public DriveSharingModel SharingModel { get; set; }

    /// <summary>The drive's existing key, locked with the zero-knowledge master key.</summary>
    public string ZkMasterKeyEncryptedStorageKey { get; set; } = "";

    /// <summary>The new write-only public key, as the browser exports ECC public keys.</summary>
    public string WriteOnlyPublicKeyJwk { get; set; } = "";

    /// <summary>The new write-only private key (PKCS8), locked with the drive key.</summary>
    public string ZkStorageKeyEncryptedWriteOnlyPrivateKey { get; set; } = "";

    /// <summary>Each app granted the drive: its copy of the drive key, and its zero-knowledge key where it had none.</summary>
    public List<ZkAppKeysRequest> Apps { get; set; } = [];

    public void Validate()
    {
        if (DriveId == Guid.Empty)
        {
            throw new OdinClientException("A drive id is required", OdinClientErrorCode.ArgumentError);
        }

        if (!SharingModel.IsZeroKnowledge())
        {
            throw new OdinClientException($"{nameof(SharingModel)} is Private or SelectiveShare", OdinClientErrorCode.ArgumentError);
        }

        // The limits are the Drives columns'
        ZkBlob.AssertLocked(ZkMasterKeyEncryptedStorageKey, nameof(ZkMasterKeyEncryptedStorageKey), maxLength: 1024);
        ZkBlob.AssertLocked(ZkStorageKeyEncryptedWriteOnlyPrivateKey, nameof(ZkStorageKeyEncryptedWriteOnlyPrivateKey), maxLength: 4096);
        ZkBlob.AssertPublicKey(WriteOnlyPublicKeyJwk, nameof(WriteOnlyPublicKeyJwk));
        if (Apps == null)
        {
            throw new OdinClientException($"{nameof(Apps)} is required (empty for none)", OdinClientErrorCode.ArgumentError);
        }

        foreach (var app in Apps)
        {
            if (app == null)
            {
                throw new OdinClientException($"{nameof(Apps)} holds an empty entry", OdinClientErrorCode.ArgumentError);
            }

            app.Validate();
        }

        if (Apps.Select(a => a.AppId).Distinct().Count() != Apps.Count)
        {
            throw new OdinClientException("Each app appears once", OdinClientErrorCode.ArgumentError);
        }
    }
}
