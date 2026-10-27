#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
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
/// No file is re-encrypted.  Vault only, for now.
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

        var drive = await GetConvertibleDriveAsync(request.DriveId);
        var writeOnlyPublicKey = DriveWriteOnlyKey.FromPublicKeyJwk(request.WriteOnlyPublicKeyJwk);

        int relabelled;
        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await driveManager.ApplyZeroKnowledgeKeysKeepingKeyAsync(drive.Id, request.ZkMasterKeyEncryptedStorageKey,
                writeOnlyPublicKey, request.ZkStorageKeyEncryptedWriteOnlyPrivateKey, odinContext);

            await appRegistrationService.SetZkAppKeysForConversionAsync(drive.Id, request.Apps, odinContext);

            await appRegistrationService.RemoveStorageKeyFromAllAppsAsync(drive.Id, odinContext);
            await circleNetworkService.RemoveStorageKeyFromAllGrantsAsync(drive.Id, odinContext);
            await circleNetworkRequestService.RemoveStorageKeyFromSentRequestsAsync(drive.Id, odinContext);

            relabelled = await RelabelKeyHeadersAsync(drive.Id);

            // Cached contexts carry grants with the key, and the apps' contexts no copies yet
            tx.AddPostCommitAction(() => contextCache.ResetAsync());
            tx.Commit();
        }

        logger.LogInformation("Drive {driveId} is now zero knowledge and kept its key; {count} key headers relabelled zkDrive",
            drive.Id, relabelled);
    }

    /// <summary>
    /// Every file whose key header the server locked with this drive's key becomes <c>zkDrive</c>: version 1 of
    /// <c>zkDrive</c> is the same 48 bytes as <c>aes</c> under the drive key (EncryptedKeyHeader), so only the type changes
    /// and nothing is opened.  The version tag and <c>modified</c> don't move (<c>RawUpdateAsync</c> writes the record's
    /// own), so no client resyncs.  An unencrypted file's empty key header (a zero IV) stays <c>aes</c>; a deleted file's
    /// tombstone keeps its real key header, and is relabelled with the rest.
    /// </summary>
    private async Task<int> RelabelKeyHeadersAsync(Guid driveId)
    {
        var count = 0;
        foreach (var record in await driveMainIndex.GetAllByDriveIdAsync(driveId))
        {
            var keyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(record.hdrEncryptedKeyHeader);
            if (keyHeader is not { Type: EncryptionType.Aes } || keyHeader.Iv?.Any(b => b != 0) != true)
            {
                continue;
            }

            keyHeader.Type = EncryptionType.ZkDrive;
            record.hdrEncryptedKeyHeader = OdinSystemSerializer.Serialize(keyHeader);
            await driveMainIndex.RawUpdateAsync(record);
            count++;
        }

        return count;
    }

    /// <summary>Vault, not yet zero knowledge: the one drive converted this way for now (plan 3.7).</summary>
    private async Task<StorageDrive> GetConvertibleDriveAsync(Guid driveId)
    {
        var drive = await driveManager.GetDriveAsync(driveId)
                    ?? throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);

        if (drive.IsZeroKnowledge)
        {
            throw new OdinClientException($"Drive {driveId} is already zero knowledge", OdinClientErrorCode.DriveCannotBeConverted);
        }

        if (driveId != WellKnownAppDrives.VaultDrive.Alias.Value)
        {
            throw new OdinClientException("Only the Vault drive converts to zero knowledge for now",
                OdinClientErrorCode.DriveCannotBeConverted);
        }

        return drive;
    }
}

/// <summary>What the browser made to convert a drive that keeps its key (docs/zero-knowledge-drive-api-plan.md, 3.3).</summary>
public sealed class ZkConvertDriveKeepingKeyRequest
{
    public Guid DriveId { get; set; }

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

        // The limits are the Drives columns'
        ZkBlob.AssertLocked(ZkMasterKeyEncryptedStorageKey, nameof(ZkMasterKeyEncryptedStorageKey), maxLength: 1024);
        ZkBlob.AssertLocked(ZkStorageKeyEncryptedWriteOnlyPrivateKey, nameof(ZkStorageKeyEncryptedWriteOnlyPrivateKey), maxLength: 4096);
        ZkBlob.AssertPublicKey(WriteOnlyPublicKeyJwk, nameof(WriteOnlyPublicKeyJwk));
        foreach (var app in Apps ?? [])
        {
            app.Validate();
        }
    }
}
