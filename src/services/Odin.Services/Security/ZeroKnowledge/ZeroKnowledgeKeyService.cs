using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;

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
    StandardFileSystem fileSystem,
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

        // The phrase and friend copies lock the zero-knowledge master key, which a password change
        // keeps.  Each password copy is the new password's, or none.
        AssertSealedTo(request.PhrasePublicKeyEncryptedPassword, existing.PhrasePublicKeyJwk);
        AssertSealedTo(request.ShamirPublicKeyEncryptedPassword, existing.ShamirPublicKeyJwk);
        await SaveRecordAsync(existing with
        {
            ZkSaltKek64 = request.ZkSaltKek64,
            ZkKekEncryptedZkMasterKey = request.ZkKekEncryptedZkMasterKey,
            PhrasePublicKeyEncryptedPassword = existing.PhrasePublicKeyJwk == null ? null : request.PhrasePublicKeyEncryptedPassword,
            ShamirPublicKeyEncryptedPassword = existing.ShamirPublicKeyJwk == null ? null : request.ShamirPublicKeyEncryptedPassword
        });
    }

    /// <summary>Writes the password copies given; the caller proved the password.</summary>
    internal async Task SavePasswordAsync(ZkPasswordRequest request)
    {
        var existing = await GetRecordOrFailAsync();
        if (request.PhrasePublicKeyEncryptedPassword != null && existing.PhrasePublicKeyJwk == null)
        {
            throw new OdinClientException("No phrase key to seal the password to", OdinClientErrorCode.RecoveryPhraseNotTakenOver);
        }

        if (request.ShamirPublicKeyEncryptedPassword != null && existing.ShamirPublicKeyJwk == null)
        {
            throw new OdinClientException("No friend deal to seal the password to", OdinClientErrorCode.PasswordRecoveryNotConfigured);
        }

        AssertSealedTo(request.PhrasePublicKeyEncryptedPassword, existing.PhrasePublicKeyJwk);
        AssertSealedTo(request.ShamirPublicKeyEncryptedPassword, existing.ShamirPublicKeyJwk);

        await SaveRecordAsync(existing with
        {
            PhrasePublicKeyEncryptedPassword = request.PhrasePublicKeyEncryptedPassword ?? existing.PhrasePublicKeyEncryptedPassword,
            ShamirPublicKeyEncryptedPassword = request.ShamirPublicKeyEncryptedPassword ?? existing.ShamirPublicKeyEncryptedPassword
        });
    }

    /// <summary>
    /// Refuses a password copy sealed to a key that is no longer the phrase's or the friends' (a tab
    /// that read the keys before a new deal, say): the server can't open a copy, but it can read which
    /// key it was sealed to, and a stale one would stand in for the copy every later login writes.
    /// </summary>
    private static void AssertSealedTo(string copy64, string publicKeyJwk)
    {
        if (copy64 != null && publicKeyJwk != null && !ZkSeal.IsSealedTo(copy64.FromBase64(), publicKeyJwk))
        {
            throw new OdinClientException("The password copy is sealed to a key that is no longer current; reload and try again",
                OdinClientErrorCode.ArgumentError);
        }
    }

    /// <summary>
    /// Stores a friend deal's copies, and locks today's master key with the friends' reset key.  The
    /// friends' password copy goes: it was sealed to the last deal's key pair.
    /// </summary>
    internal async Task SaveShamirCopiesAsync(ZkShamirCopiesRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var existing = await GetRecordOrFailAsync();
        using var resetKey = request.ResetKey64.FromBase64().ToSensitiveByteArray();
        await SaveRecordAsync(existing with
        {
            ShamirPublicKeyJwk = request.ShamirPublicKeyJwk,
            ShamirKeyEncryptedShamirPrivateKey = request.ShamirKeyEncryptedShamirPrivateKey,
            ShamirKeyEncryptedZkMasterKey = request.ShamirKeyEncryptedZkMasterKey,
            ShamirPublicKeyEncryptedPassword = null,
            ShamirResetKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(secret: resetKey, dataToEncrypt: odinContext.Caller.GetMasterKey())
        });
    }

    /// <summary>Forgets a friend deal's copies, when the deal goes.</summary>
    internal async Task ForgetShamirCopiesAsync()
    {
        var existing = await GetRecordAsync();
        if (existing?.ShamirPublicKeyJwk == null && existing?.ShamirResetKeyEncryptedMasterKey == null)
        {
            return;
        }

        await SaveRecordAsync(existing with
        {
            ShamirPublicKeyJwk = null,
            ShamirKeyEncryptedShamirPrivateKey = null,
            ShamirKeyEncryptedZkMasterKey = null,
            ShamirPublicKeyEncryptedPassword = null,
            ShamirResetKeyEncryptedMasterKey = null
        });
    }

    internal async Task<bool> HasShamirCopiesAsync() => (await GetRecordAsync())?.HasShamirCopies() == true;

    /// <summary>A friend deal's copies for recovery with friends; null without a deal that can recover.</summary>
    internal async Task<ZkShamirRecoveryCopies> GetShamirRecoveryCopiesAsync()
    {
        var record = await GetRecordAsync();
        if (record?.HasShamirCopies() != true)
        {
            return null;
        }

        return new ZkShamirRecoveryCopies
        {
            ShamirPublicKeyJwk = record.ShamirPublicKeyJwk,
            ShamirKeyEncryptedShamirPrivateKey = record.ShamirKeyEncryptedShamirPrivateKey,
            ShamirKeyEncryptedZkMasterKey = record.ShamirKeyEncryptedZkMasterKey,
            ShamirPublicKeyEncryptedPassword = record.ShamirPublicKeyEncryptedPassword
        };
    }

    /// <summary>
    /// Stores what the browser made from the phrase, and locks today's master key with its reset key;
    /// the caller validates it and decides when.  The password copy is sealed to the new public key,
    /// or none.
    /// </summary>
    internal async Task SavePhraseCopiesAsync(ZkPhraseCopiesRequest request, string phrasePublicKeyEncryptedPassword,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var existing = await GetRecordOrFailAsync();
        AssertSealedTo(phrasePublicKeyEncryptedPassword, request.PhrasePublicKeyJwk);
        using var resetKey = request.ResetKey64.FromBase64().ToSensitiveByteArray();
        await SaveRecordAsync(existing with
        {
            PhraseKeyEncryptedZkMasterKey = request.PhraseKeyEncryptedZkMasterKey,
            ZkMasterKeyEncryptedPhraseKey = request.ZkMasterKeyEncryptedPhraseKey,
            PhrasePublicKeyJwk = request.PhrasePublicKeyJwk,
            PhraseKeyEncryptedPhrasePrivateKey = request.PhraseKeyEncryptedPhrasePrivateKey,
            PhrasePublicKeyEncryptedPassword = phrasePublicKeyEncryptedPassword,
            ResetKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(secret: resetKey, dataToEncrypt: odinContext.Caller.GetMasterKey())
        });
    }

    /// <summary>
    /// Today's master key, from the reset key that recovery with the phrase or with friends sends.
    /// Refused (a key hash mismatch, 403) for any other key.
    /// </summary>
    internal async Task<SensitiveByteArray> OpenMasterKeyWithResetKeyAsync(SensitiveByteArray resetKey)
    {
        var record = await GetRecordAsync();
        var resetCopies = new[]
        {
            record?.HasPhraseCopies() == true ? record.ResetKeyEncryptedMasterKey : null,
            record?.HasShamirCopies() == true ? record.ShamirResetKeyEncryptedMasterKey : null
        }.Where(copy => copy != null).ToList();

        if (resetCopies.Count == 0)
        {
            throw new OdinClientException("Neither the recovery phrase nor friends can reset the password",
                OdinClientErrorCode.RecoveryPhraseNotTakenOver);
        }

        foreach (var copy in resetCopies)
        {
            try
            {
                return copy.DecryptKeyClone(resetKey);
            }
            catch (OdinSecurityException) when (copy != resetCopies[^1])
            {
                // not this one's key; the last copy's refusal is the answer
            }
        }

        throw new UnreachableException();
    }

    /// <summary>
    /// What recovery with the phrase fetches, with no login.  Null unless the phrase copies exist.
    /// </summary>
    public async Task<ZkPhraseRecoveryResponse> GetPhraseRecoveryAsync()
    {
        var record = await GetRecordAsync();
        if (record?.HasPhraseCopies() != true)
        {
            return null;
        }

        return new ZkPhraseRecoveryResponse
        {
            PhraseKeyEncryptedZkMasterKey = record.PhraseKeyEncryptedZkMasterKey,
            PhraseKeyEncryptedPhrasePrivateKey = record.PhraseKeyEncryptedPhrasePrivateKey,
            PhrasePublicKeyJwk = record.PhrasePublicKeyJwk,
            PhrasePublicKeyEncryptedPassword = record.PhrasePublicKeyEncryptedPassword
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
    /// with the recovery key, say) because the server never encrypts a file on it; what the browser
    /// locked with the old keys (deposits sealed to the old write-only key, files locked with the old
    /// drive key) can't be opened again, and is deleted.
    /// </summary>
    public async Task ConvertShardRecoveryDriveAsync(ZkConvertDriveRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();

        await GetRecordOrFailAsync();

        var writeOnlyPublicKey = DriveWriteOnlyKey.FromPublicKeyJwk(request.WriteOnlyPublicKeyJwk);

        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias.Value;

        List<ServerFileHeader> dead;
        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            // Nothing the server locked may use the key being dropped.  Shamir writes only unencrypted files,
            // deposits and the browser's own files here, so this only fails if something else wrote there.
            var files = (await driveMainIndex.GetAllByDriveIdAsync(driveId)).Select(ServerFileHeader.FromDriveMainIndexRecord).ToList();
            var lockedByTheDriveKey = files.Count(h => h.FileMetadata.IsEncrypted && !h.EncryptedKeyHeader.IsLockedByClient());
            if (lockedByTheDriveKey > 0)
            {
                throw new OdinClientException(
                    $"The Shard Recovery drive holds {lockedByTheDriveKey} encrypted file(s); it cannot be given new keys",
                    OdinClientErrorCode.DriveHasEncryptedFiles);
            }

            // Deposits are sealed to the write-only key being dropped, so nothing can open them again: a
            // friend's shard dies with their reset, and the dealer is told to deal again (its check sees
            // the shard gone).  The requests to release them go too: only gated shards, all deposits, get one.
            // So do the browser's own files, locked with the drive key being dropped.
            dead = files.Where(h =>
                    (h.FileMetadata.IsEncrypted && h.EncryptedKeyHeader.IsLockedByClient()) || ShardRequestApprovalCollector.IsReleaseRequest(h))
                .ToList();

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
        }

        // Only once the new keys are committed: a delete takes a file's payloads with it at once, which a
        // conversion that didn't commit couldn't undo.  One left behind is harmless: nothing opens it, and a
        // dealer's check sees a deposit sealed to an old key as not held.  So a failure is logged, not thrown
        // (the conversion has happened), and a file written again since the snapshot is left alone.
        var ownerContext = OdinContextUpgrades.UpgradeToByPassAclCheck(WellKnownAppDrives.ShardRecoveryDrive,
            DrivePermission.ReadWrite, odinContext);
        foreach (var file in dead)
        {
            try
            {
                await fileSystem.Storage.HardDeleteLongTermFile(file.FileMetadata.File,
                    file.FileMetadata.VersionTag.GetValueOrDefault(), ownerContext);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Converting the Shard Recovery drive left the dead file {fileId} behind",
                    file.FileMetadata.File.FileId);
            }
        }

        logger.LogInformation("Shard Recovery drive converted to zero knowledge");
    }

    private async Task<ZkKeyRecord> GetRecordAsync()
    {
        return await Storage.GetAsync<ZkKeyRecord>(tblKeyValue, ZkKeyStorageId);
    }

    internal async Task AssertKeysExistAsync()
    {
        await GetRecordOrFailAsync();
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
        Created = UnixTimeUtc.Now(),
        Updated = UnixTimeUtc.Now()
    };
}
