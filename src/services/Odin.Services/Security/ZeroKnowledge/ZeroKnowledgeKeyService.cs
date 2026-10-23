using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// Stores the owner's zero-knowledge keys and converts the Shard Recovery drive to zero knowledge
/// (docs/zero-knowledge-keys-plan.md).
/// </summary>
/// <remarks>
/// The browser makes and opens every key here; the server only stores what it is given.  Nothing
/// here ever holds a key it could open.
/// </remarks>
public class ZeroKnowledgeKeyService(
    ILogger<ZeroKnowledgeKeyService> logger,
    TableKeyValueCached tblKeyValue,
    DriveManager driveManager,
    TableDriveMainIndexCached driveMainIndex,
    OdinContextCache contextCache,
    IdentityDatabase db)
{
    private static readonly Guid ZkKeyStorageId = Guid.Parse("4f1c2b8e-7d3a-4c6b-9e5f-1a2b3c4d5e6f");
    private const string ZkKeyContextKey = "b3e7c1d2-5a4f-4e8b-9c6d-7f8a9b0c1d2e";

    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ZkKeyContextKey));

    public async Task<ZkStatusResponse> GetStatusAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var record = await GetRecordAsync();
        var shardDrive = await driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias);

        return new ZkStatusResponse
        {
            ZkSaltKek64 = record?.ZkSaltKek64,
            ZkKekEncryptedZkMasterKey = record?.ZkKekEncryptedZkMasterKey,
            ShardRecoveryDriveZkMasterKeyEncryptedStorageKey = shardDrive?.ZkMasterKeyEncryptedStorageKey
        };
    }

    /// <summary>
    /// Saves the keys the browser made at the owner's first login since this shipped.  Refuses when
    /// keys exist: replacing them would strand everything locked by the current master key.
    /// </summary>
    public async Task CreateAsync(ZkKeysRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();

        // Insert-if-absent, not check-then-write: two first logins at once must not both win.
        if (!await Storage.TryInsertAsync(tblKeyValue, ZkKeyStorageId, ToRecord(request, created: UnixTimeUtc.Now())))
        {
            throw new OdinClientException("Zero-knowledge keys are already set",
                OdinClientErrorCode.ZeroKnowledgeKeysAlreadySet);
        }
    }

    /// <summary>
    /// The password change half: the browser re-locked the master key with the new password and
    /// worked out the new login keys.  Called by <c>OwnerSecretService.ResetPasswordAsync</c> in the
    /// same transaction as the password itself.  Nothing to do before the browser has made the keys;
    /// once it has, a password change without them would leave them locked by the old password.
    /// </summary>
    public async Task RelockForPasswordChangeAsync(ZkKeysRequest request)
    {
        var existing = await GetRecordAsync();
        if (existing == null)
        {
            return;
        }

        if (request == null)
        {
            throw new OdinClientException(
                "The zero-knowledge keys must be re-locked with the new password in the same request",
                OdinClientErrorCode.ZeroKnowledgeKeysRequired);
        }

        request.Validate();
        await Storage.UpsertAsync(tblKeyValue, ZkKeyStorageId, ToRecord(request, existing.Created));
    }

    /// <summary>
    /// Forgets the keys after a reset with the recovery key: they are locked by the forgotten
    /// password and can never be opened again.  The browser makes new ones at the next login.
    /// </summary>
    public async Task DeleteAsync()
    {
        await Storage.DeleteAsync(tblKeyValue, ZkKeyStorageId);
    }

    /// <summary>
    /// Gives the Shard Recovery drive keys made in the browser.  Allowed again later (after a reset
    /// with the recovery key, say) because the drive never holds a file encrypted with its key.
    /// </summary>
    public async Task ConvertShardRecoveryDriveAsync(ZkConvertDriveRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();

        if (await GetRecordAsync() == null)
        {
            throw new OdinClientException("Make the zero-knowledge keys before converting a drive",
                OdinClientErrorCode.ZeroKnowledgeKeysNotSet);
        }

        EccFullKeyData writeOnlyPublicKey;
        try
        {
            writeOnlyPublicKey = DriveWriteOnlyKey.FromPublicKeyJwk(request.WriteOnlyPublicKeyJwk);
        }
        catch (Exception e) when (e is not OdinClientException)
        {
            throw new OdinClientException("WriteOnlyPublicKeyJwk is not a valid public key", OdinClientErrorCode.ArgumentError);
        }

        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias.Value;

        await using var tx = await db.BeginStackedTransactionAsync();

        // Nothing may use the key being dropped.  Every file Shamir writes on this drive is
        // unencrypted, so this only fails if something else wrote there.
        var files = await driveMainIndex.GetAllByDriveIdAsync(driveId);
        var encrypted = files
            .Select(ServerFileHeader.FromDriveMainIndexRecord)
            .Count(h => h.FileMetadata.IsEncrypted);
        if (encrypted > 0)
        {
            throw new OdinClientException(
                $"The Shard Recovery drive holds {encrypted} encrypted file(s); it cannot be given new keys",
                OdinClientErrorCode.DriveHasEncryptedFiles);
        }

        await driveManager.ApplyZeroKnowledgeKeysAsync(
            driveId,
            request.ZkMasterKeyEncryptedStorageKey,
            request.EncryptedIdIv64.FromBase64(),
            request.EncryptedIdValue64.FromBase64(),
            writeOnlyPublicKey,
            request.ZkStorageKeyEncryptedWriteOnlyPrivateKey,
            odinContext);

        // Cached owner contexts carry a grant with the old key; drop them once this is committed.
        tx.AddPostCommitAction(() => contextCache.ResetAsync());
        tx.Commit();

        logger.LogInformation("Shard Recovery drive converted to zero knowledge");
    }

    private async Task<ZkKeyRecord> GetRecordAsync()
    {
        return await Storage.GetAsync<ZkKeyRecord>(tblKeyValue, ZkKeyStorageId);
    }

    private static ZkKeyRecord ToRecord(ZkKeysRequest request, UnixTimeUtc created) => new()
    {
        ZkSaltKek64 = request.ZkSaltKek64,
        ZkKekEncryptedZkMasterKey = request.ZkKekEncryptedZkMasterKey,
        ZkMasterKeyEncryptedLoginKeys = request.ZkMasterKeyEncryptedLoginKeys,
        Created = created,
        Updated = UnixTimeUtc.Now()
    };
}
