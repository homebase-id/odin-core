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

    /// <summary>The keys part of the status; <c>ZeroKnowledgeRecoveryService</c> adds the phrase's state.</summary>
    public async Task<ZkKeyRecord> GetKeysAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        return await GetRecordAsync();
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
        if (!await Storage.TryInsertAsync(tblKeyValue, ZkKeyStorageId, ToRecord(request)))
        {
            throw new OdinClientException("Zero-knowledge keys are already set",
                OdinClientErrorCode.ZeroKnowledgeKeysAlreadySet);
        }
    }

    /// <summary>
    /// The password change half: the browser re-locked the zero-knowledge master key with the new
    /// password.  Called by <c>OwnerSecretService</c> in the same transaction as the password itself,
    /// for a password change and for a reset with the phrase's reset key.  Nothing to do before the
    /// browser has made the keys; once it has, a password change without them would leave them
    /// locked by the old password.
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

        // The phrase copies lock the zero-knowledge master key, which a password change keeps.
        await SaveRecordAsync(existing with
        {
            ZkSaltKek64 = request.ZkSaltKek64,
            ZkKekEncryptedZkMasterKey = request.ZkKekEncryptedZkMasterKey,
            ZkMasterKeyEncryptedPassword = request.ZkMasterKeyEncryptedPassword
        });
    }

    /// <summary>Writes the password copy; the caller proved the password.</summary>
    internal async Task SavePasswordAsync(string zkMasterKeyEncryptedPassword)
    {
        var existing = await GetRecordOrFailAsync();
        await SaveRecordAsync(existing with { ZkMasterKeyEncryptedPassword = zkMasterKeyEncryptedPassword });
    }

    /// <summary>
    /// Stores the browser's two phrase copies, and locks today's master key with its reset key; the
    /// caller validates them and decides when.
    /// </summary>
    internal async Task SavePhraseCopiesAsync(ZkPhraseCopiesRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var existing = await GetRecordOrFailAsync();
        using var resetKey = request.ResetKey64.FromBase64().ToSensitiveByteArray();
        await SaveRecordAsync(existing with
        {
            PhraseKeyEncryptedZkMasterKey = request.PhraseKeyEncryptedZkMasterKey,
            ZkMasterKeyEncryptedPhraseKey = request.ZkMasterKeyEncryptedPhraseKey,
            ResetKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(secret: resetKey, dataToEncrypt: odinContext.Caller.GetMasterKey())
        });
    }

    /// <summary>
    /// Today's master key, from the reset key recovery with the phrase sends.  Refused (a key hash
    /// mismatch) for any other key.
    /// </summary>
    internal async Task<SensitiveByteArray> OpenMasterKeyWithResetKeyAsync(string resetKey64)
    {
        var record = await GetRecordAsync();
        if (record?.ResetKeyEncryptedMasterKey == null)
        {
            throw new OdinClientException("The recovery phrase was not taken over", OdinClientErrorCode.RecoveryPhraseNotTakenOver);
        }

        using var resetKey = resetKey64.FromBase64().ToSensitiveByteArray();
        return record.ResetKeyEncryptedMasterKey.DecryptKeyClone(resetKey);
    }

    /// <summary>
    /// What recovery with the phrase fetches, with no login.  Null unless the phrase was taken over
    /// and the password copy exists.
    /// </summary>
    public async Task<ZkPhraseRecoveryResponse> GetPhraseRecoveryAsync()
    {
        var record = await GetRecordAsync();
        if (record?.PhraseKeyEncryptedZkMasterKey == null || record.ZkMasterKeyEncryptedPassword == null)
        {
            return null;
        }

        return new ZkPhraseRecoveryResponse
        {
            PhraseKeyEncryptedZkMasterKey = record.PhraseKeyEncryptedZkMasterKey,
            ZkMasterKeyEncryptedPassword = record.ZkMasterKeyEncryptedPassword
        };
    }

    /// <summary>
    /// Forgets the keys after a reset with today's master key: they are locked by the forgotten
    /// password and can never be opened again, and so is every copy tied to the zero-knowledge master
    /// key.  The browser makes new ones at the next login.
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

        await GetRecordOrFailAsync();

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

    private async Task<ZkKeyRecord> GetRecordOrFailAsync()
    {
        return await GetRecordAsync() ?? throw new OdinClientException("Make the zero-knowledge keys first",
            OdinClientErrorCode.ZeroKnowledgeKeysNotSet);
    }

    private async Task SaveRecordAsync(ZkKeyRecord record)
    {
        await Storage.UpsertAsync(tblKeyValue, ZkKeyStorageId, record with { Updated = UnixTimeUtc.Now() });
    }

    private static ZkKeyRecord ToRecord(ZkKeysRequest request) => new()
    {
        ZkSaltKek64 = request.ZkSaltKek64,
        ZkKekEncryptedZkMasterKey = request.ZkKekEncryptedZkMasterKey,
        ZkMasterKeyEncryptedPassword = request.ZkMasterKeyEncryptedPassword,
        Created = UnixTimeUtc.Now(),
        Updated = UnixTimeUtc.Now()
    };
}
