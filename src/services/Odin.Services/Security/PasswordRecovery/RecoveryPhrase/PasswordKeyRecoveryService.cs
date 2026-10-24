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
            await ConfirmInitialRecoveryKeyStorage(odinContext);
            var leKeyNow = await GetRecoveryKeyAsync(true, odinContext);
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
            if (recoveryKeyRecord.TakenOver != null)
            {
                // The browser opens this copy and shows the words; the server can't.
                var zkKeys = await zeroKnowledgeKeyService.GetKeysAsync(odinContext);
                await ClearNextViewableDate();
                return new RecoveryKeyResult
                {
                    ZkMasterKeyEncryptedPhraseKey = zkKeys?.ZkMasterKeyEncryptedPhraseKey,
                    Created = recoveryKeyRecord.Created,
                    HasInitiallyReviewedKey = recoveryKeyRecord.InitialRecoveryKeyViewingDate != null
                };
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var recoverKey = recoveryKeyRecord.MasterKeyEncryptedRecoverKey.DecryptKeyClone(masterKey);

            var readableText = BIP39Util.GenerateBIP39(recoverKey.GetKey());

            var decryptedRecoveryKey = new RecoveryKeyResult
            {
                Key = readableText,
                Created = recoveryKeyRecord.Created,
                NextViewableDate = null, // doesnt matter
                HasInitiallyReviewedKey = recoveryKeyRecord.InitialRecoveryKeyViewingDate != null
            };

            await ClearNextViewableDate();
            recoverKey.Wipe();
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
        var hash = ByteArrayUtil.ReduceSHA256Hash(recoveryInfo?.Email);
        return hash;
    }

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
        zeroKnowledgeKeyService.AssertRecentPasswordLogin(odinContext);
        var record = await GetRecordForTakeoverAsync();

        using var phraseKey = record.MasterKeyEncryptedRecoverKey.DecryptKeyClone(odinContext.Caller.GetMasterKey());
        return BIP39Util.GenerateBIP39(phraseKey.GetKey());
    }

    /// <summary>
    /// The server's half of the takeover, once the browser stored its copies: keeps the check value
    /// and deletes both copies the server could open.
    /// </summary>
    internal async Task CompleteTakeoverAsync(IOdinContext odinContext)
    {
        var record = await GetRecordForTakeoverAsync();

        using (var phraseKey = record.MasterKeyEncryptedRecoverKey.DecryptKeyClone(odinContext.Caller.GetMasterKey()))
        {
            record.PhraseCheck64 = RecoveryPhraseCheck.Compute(phraseKey.GetKey());
        }

        record.MasterKeyEncryptedRecoverKey = null;
        record.RecoveryKeyEncryptedMasterKey = null;
        record.TakenOver = UnixTimeUtc.Now();
        await RecoveryKeyStorage.UpsertAsync(tblKeyValue, RecordStorageId, record);
    }

    /// <summary>Refuses a phrase whose check value isn't the taken-over phrase's.</summary>
    public async Task AssertPhraseCheckAsync(string phraseCheck64)
    {
        var record = await GetKeyInternalAsync();
        if (record?.TakenOver == null)
        {
            throw new OdinClientException("The recovery phrase was not taken over",
                OdinClientErrorCode.RecoveryPhraseNotTakenOver);
        }

        var expected = record.PhraseCheck64?.FromBase64();
        byte[] given;
        try
        {
            given = phraseCheck64?.FromBase64();
        }
        catch (FormatException)
        {
            given = null;
        }

        if (expected == null || given == null ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, given))
        {
            throw new OdinClientException("That is not the recovery phrase", OdinClientErrorCode.RecoveryPhraseMismatch);
        }
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