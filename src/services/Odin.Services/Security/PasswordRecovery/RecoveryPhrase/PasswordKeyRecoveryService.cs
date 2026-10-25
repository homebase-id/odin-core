using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Security.Email;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class PasswordKeyRecoveryService(
    TableKeyValueCached tblKeyValue,
    TenantContext tenantContext,
    RecoveryNotifier recoveryNotifier,
    OdinConfiguration configuration,
    ZeroKnowledgeKeyService zeroKnowledgeKeyService)
{
    private static readonly Guid RecordStorageId = Guid.Parse("7fd3665e-957f-4846-a437-61c3d76fc262");
    private const string ContextKey = "3780295a-5bc6-4e0f-8334-4b5c063099c4";

    private static readonly SingleKeyValueStorage RecoveryKeyStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    private readonly Guid _accountRecoveryInfoStorageId = Guid.Parse("44879725-b01f-4aab-8c84-921c63df087a");
    private const string AccountRecoveryInfoContextKey = "94db6882-aa92-44de-8da8-a23c15a11d88";

    private static readonly SingleKeyValueStorage AccountRecoveryInfoStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(AccountRecoveryInfoContextKey));


    /// <summary>
    /// Validates the recovery key and returns the decrypted master key, if valid.
    /// </summary>
    public async Task<SensitiveByteArray> AssertValidKeyAsync(string text)
    {
        var existingKey = await GetKeyInternalAsync();
        if (existingKey?.TakenOver != null)
        {
            throw new OdinClientException("The recovery phrase now recovers the password in the browser",
                OdinClientErrorCode.RecoveryPhraseTakenOver);
        }

        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        var key = BIP39Util.DecodeBIP39(text);
        return existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(key);
    }

    public async Task CreateInitialKeyAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var keyRecord = await RecoveryKeyStorage.GetAsync<RecoveryKeyRecord>(tblKeyValue, RecordStorageId);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        var keyBytes = ByteArrayUtil.GetRndByteArray(16);
        await SaveKeyAsync(keyBytes.ToSensitiveByteArray(), odinContext);
    }

    public async Task<RequestRecoveryKeyResult> RequestRecoveryKey(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var recoveryKey = await GetKeyInternalAsync();

        // if they have never viewed the recovery key, allow us to see the key now
        if (recoveryKey?.InitialRecoveryKeyViewingDate == null)
        {
            // The reveal first: a reveal that fails (the phrase waits to be typed in again) must not
            // spend the one free view.
            var leKeyNow = await GetRecoveryKeyAsync(true, odinContext);
            await ConfirmInitialRecoveryKeyStorage(odinContext);
            return new RequestRecoveryKeyResult()
            {
                Key = leKeyNow.Key,
                ZkMasterKeyEncryptedPhraseKey = leKeyNow.ZkMasterKeyEncryptedPhraseKey,
                NextViewableDate = UnixTimeUtc.ZeroTime
            };
        }

        // if they have viewed the key before, then we need to wait for the waiting period
        var nextDate = UnixTimeUtc.Now().AddMilliseconds((Int64)GetWaitingPeriod().TotalMilliseconds);
        return new RequestRecoveryKeyResult()
        {
            NextViewableDate = await MarkNextViewableDate(nextDate)
        };
    }

    public async Task ConfirmInitialRecoveryKeyStorage(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var recoveryKey = await GetKeyInternalAsync();
        if (null == recoveryKey.InitialRecoveryKeyViewingDate)
        {
            recoveryKey.InitialRecoveryKeyViewingDate = UnixTimeUtc.Now();
            await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, recoveryKey);
        }
    }

    public async Task<bool> HasRecoveryKeyBeenViewed()
    {
        var keyRecord = await GetKeyInternalAsync();
        return keyRecord?.InitialRecoveryKeyViewingDate != null;
    }

    public async Task<RecoveryKeyResult> GetRecoveryKeyAsync(bool byPassWaitingPeriod, IOdinContext odinContext)
    {
        var ctx = odinContext;
        ctx.Caller.AssertHasMasterKey();
        if (ctx.AuthTokenCreated == null)
        {
            throw new OdinSecurityException("Could not validate token creation date");
        }

        async Task<RecoveryKeyResult> DecryptedRecoveryKey(RecoveryKeyRecord recoveryKeyRecord)
        {
            // Once taken over, the browser opens its copy and shows the words; the server can't.
            var takenOver = recoveryKeyRecord.TakenOver != null;
            var keys = takenOver ? await zeroKnowledgeKeyService.GetKeysAsync(odinContext) : null;
            if (takenOver && keys?.HasPhraseCopies() != true)
            {
                // The waiting period is left as it was.
                throw new OdinClientException("Type the recovery phrase in again to keep it",
                    OdinClientErrorCode.RecoveryPhraseNeedsReentry);
            }

            var decryptedRecoveryKey = new RecoveryKeyResult
            {
                Key = takenOver ? null : OpenPhrase(recoveryKeyRecord, odinContext),
                ZkMasterKeyEncryptedPhraseKey = keys?.ZkMasterKeyEncryptedPhraseKey,
                Created = recoveryKeyRecord.Created,
                NextViewableDate = null, // doesnt matter
                HasInitiallyReviewedKey = recoveryKeyRecord.InitialRecoveryKeyViewingDate != null
            };

            await ClearNextViewableDate();
            return decryptedRecoveryKey;
        }

        var keyRecord = await GetKeyInternalAsync();
        if (byPassWaitingPeriod) // short circuit
        {
            return await DecryptedRecoveryKey(keyRecord);
        }

        if (keyRecord.NextViewableDate == null)
        {
            return new RecoveryKeyResult
            {
                Key = null,
                Created = default,
                NextViewableDate = null,
                HasInitiallyReviewedKey = keyRecord.InitialRecoveryKeyViewingDate != null
            };
        }

        if (UnixTimeUtc.Now() < keyRecord.NextViewableDate.Value)
        {
            return new RecoveryKeyResult
            {
                Key = null,
                Created = default,
                NextViewableDate = keyRecord.NextViewableDate,
                HasInitiallyReviewedKey = keyRecord.InitialRecoveryKeyViewingDate != null
            };
        }

        var rk = await DecryptedRecoveryKey(keyRecord);
        return rk;
    }

    /// <summary>
    /// Gets the official recovery email used for resetting passwords or account recovery
    /// </summary>
    public async Task<AccountRecoveryInfo> GetRecoveryInfo()
    {
        var recovery = await AccountRecoveryInfoStorage.GetAsync<AccountRecoveryInfo>(tblKeyValue, _accountRecoveryInfoStorageId);
        if (recovery != null)
        {
            return recovery;
        }

        // default
        return new AccountRecoveryInfo()
        {
            Email = tenantContext.Email,
            EmailLastVerified = null
        };
    }

    public async Task<Guid> GetHashedRecoveryEmail()
    {
        var recoveryInfo = await GetRecoveryInfo();
        OdinValidationUtils.AssertValidEmail(recoveryInfo?.Email, "Recovery email must be set to configure sharding");
        return HashRecoveryEmail(recoveryInfo?.Email);
    }

    /// <summary>
    /// The recovery email as each shard stores it at the deal: an automatic holder checks the address
    /// a dealer sends against it before it emails a link there.
    /// </summary>
    public static Guid HashRecoveryEmail(string email) => ByteArrayUtil.ReduceSHA256Hash(email);

    public async Task UpdateAccountRecoveryEmail(Guid nonceId)
    {
        var email = await recoveryNotifier.GetNonceDataOrFail(nonceId);
        var recovery = await GetAccountRecoveryInfo() ?? new AccountRecoveryInfo();
        recovery.Email = email;
        recovery.EmailLastVerified = UnixTimeUtc.Now();
        
        await AccountRecoveryInfoStorage.UpsertAsync(tblKeyValue, _accountRecoveryInfoStorageId, recovery);
    }

    private async Task<AccountRecoveryInfo> GetAccountRecoveryInfo()
    {
        var recovery = await AccountRecoveryInfoStorage.GetAsync<AccountRecoveryInfo>(tblKeyValue, _accountRecoveryInfoStorageId);
        return recovery;
    }

    /// <summary>Whether a phrase exists, and whether the browser took it over.</summary>
    public async Task<(bool Exists, bool TakenOver)> GetPhraseStateAsync()
    {
        var record = await GetKeyInternalAsync();
        return (record != null, record?.TakenOver != null);
    }

    /// <summary>
    /// Hands the phrase to the browser once, for the takeover.  Refused once taken over: from then on
    /// the server has no copy to hand over.
    /// </summary>
    public async Task<string> GetPhraseForTakeoverAsync(IOdinContext odinContext)
    {
        return OpenPhrase(await GetRecordForTakeoverAsync(), odinContext);
    }

    /// <summary>Refuses unless the server still holds the phrase for a takeover.</summary>
    internal async Task AssertNotTakenOverAsync()
    {
        await GetRecordForTakeoverAsync();
    }

    /// <summary>
    /// The server's half of the takeover, once the browser stored its copies: keeps the check value,
    /// which the caller verified with <see cref="AssertPhraseCheckAsync"/>, and deletes both copies the
    /// server could open.
    /// </summary>
    internal async Task CompleteTakeoverAsync(string verifiedPhraseCheck64)
    {
        var record = await GetRecordForTakeoverAsync();
        record.PhraseCheck64 = verifiedPhraseCheck64;
        record.MasterKeyEncryptedRecoverKey = null;
        record.RecoveryKeyEncryptedMasterKey = null;
        record.TakenOver = UnixTimeUtc.Now();
        await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, record);
    }

    /// <summary>
    /// Refuses a phrase whose check value isn't the owner's phrase's.  The browser only ever sends the
    /// check value: before the takeover the server works out the expected one from its own copy.
    /// </summary>
    public async Task AssertPhraseCheckAsync(string phraseCheck64, IOdinContext odinContext)
    {
        var given = ZkBlob.AssertBase64(phraseCheck64, nameof(phraseCheck64), exactLength: RecoveryPhraseCheck.Length);

        var record = await GetKeyInternalAsync() ?? throw new OdinClientException("No recovery phrase",
            OdinClientErrorCode.NotInitialized);
        var expected = (record.PhraseCheck64 ?? ComputePhraseCheck(record, odinContext)).FromBase64();

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, given))
        {
            throw new OdinClientException("That is not the recovery phrase", OdinClientErrorCode.RecoveryPhraseMismatch);
        }
    }

    private static string ComputePhraseCheck(RecoveryKeyRecord record, IOdinContext odinContext)
    {
        using var phraseKey = OpenPhraseKey(record, odinContext);
        return RecoveryPhraseCheck.Compute(phraseKey.GetKey());
    }

    private static string OpenPhrase(RecoveryKeyRecord record, IOdinContext odinContext)
    {
        using var phraseKey = OpenPhraseKey(record, odinContext);
        return BIP39Util.GenerateBIP39(phraseKey.GetKey());
    }

    /// <summary>The server's own copy of the phrase, while it still has one.</summary>
    private static SensitiveByteArray OpenPhraseKey(RecoveryKeyRecord record, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        return record.MasterKeyEncryptedRecoverKey.DecryptKeyClone(odinContext.Caller.GetMasterKey());
    }

    private async Task<RecoveryKeyRecord> GetRecordForTakeoverAsync()
    {
        var record = await GetKeyInternalAsync();
        if (record?.TakenOver != null)
        {
            throw new OdinClientException("The recovery phrase was already taken over",
                OdinClientErrorCode.RecoveryPhraseTakenOver);
        }

        if (record?.MasterKeyEncryptedRecoverKey == null)
        {
            throw new OdinClientException("No recovery phrase to take over", OdinClientErrorCode.NotInitialized);
        }

        return record;
    }

    private async Task<RecoveryKeyRecord> GetKeyInternalAsync()
    {
        var existingKey = await RecoveryKeyStorage.GetAsync<RecoveryKeyRecord>(tblKeyValue, RecordStorageId);
        return existingKey;
    }

    private async Task SaveKeyAsync(SensitiveByteArray recoveryKey, IOdinContext odinContext)
    {
        var masterKey = odinContext.Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(masterKey, recoveryKey),
            Created = UnixTimeUtc.Now(),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(recoveryKey, masterKey)
        };

        await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, record);
    }


    private async Task<UnixTimeUtc> MarkNextViewableDate(UnixTimeUtc nextDate)
    {
        var recoveryKey = await GetKeyInternalAsync();
        recoveryKey.NextViewableDate = nextDate;
        await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, recoveryKey);
        return nextDate;
    }

    private async Task ClearNextViewableDate()
    {
        var recoveryKey = await GetKeyInternalAsync();
        recoveryKey.NextViewableDate = null;
        await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, recoveryKey);
    }

    private TimeSpan GetWaitingPeriod()
    {
        var seconds = configuration.Development.RecoveryKeyWaitingPeriodSeconds;
        return seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : TimeSpan.FromDays(14);
    }

    public async Task StartUpdateRecoveryEmail(MailAddress email, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await recoveryNotifier.EnqueueVerifyRecoveryEmailAddress(email);
    }
}