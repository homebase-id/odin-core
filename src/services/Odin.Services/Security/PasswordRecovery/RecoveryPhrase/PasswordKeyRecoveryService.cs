using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Security.Email;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class PasswordKeyRecoveryService(
    OdinConfiguration odinConfiguration,
    TableKeyValueCached tblKeyValue,
    TenantContext tenantContext,
    RecoveryEmailer recoveryEmailer)
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

    public async Task<DecryptedRecoveryKey> GetKeyAsync(bool byPassWaitingPeriod, IOdinContext odinContext)
    {
        var ctx = odinContext;
        ctx.Caller.AssertHasMasterKey();

        if (ctx.AuthTokenCreated == null)
        {
            throw new OdinSecurityException("Could not validate token creation date");
        }

        if (!byPassWaitingPeriod)
        {
            var recoveryKeyWaitingPeriod = TimeSpan.FromDays(14);
            var recoveryKeyWaitingPeriodSeconds = odinConfiguration.Development?.RecoveryKeyWaitingPeriodSeconds ?? 10;
#if DEBUG
            recoveryKeyWaitingPeriod = TimeSpan.FromSeconds(recoveryKeyWaitingPeriodSeconds);
#endif

            if (UnixTimeUtc.Now() > ctx.AuthTokenCreated!.Value.AddMilliseconds((Int64)recoveryKeyWaitingPeriod.TotalMilliseconds))
            {
                throw new OdinSecurityException($"Cannot reveal token before {recoveryKeyWaitingPeriod.Days} days from creation");
            }
        }

        var keyRecord = await GetKeyInternalAsync();
        var masterKey = odinContext.Caller.GetMasterKey();
        var recoverKey = keyRecord.MasterKeyEncryptedRecoverKey.DecryptKeyClone(masterKey);

        var readableText = BIP39Util.GenerateBIP39(recoverKey.GetKey());

        var rk = new DecryptedRecoveryKey
        {
            Key = readableText,
            Created = keyRecord.Created
        };

        recoverKey.Wipe();
        return rk;
    }

    /// <summary>
    /// Gets the official recovery email used for resetting passwords or account recovery
    /// </summary>
    public async Task<string> GetRecoveryEmail()
    {
        var recovery = await AccountRecoveryInfoStorage.GetAsync<AccountRecoveryInfo>(tblKeyValue, _accountRecoveryInfoStorageId);
        return recovery?.Email ?? tenantContext.Email;
    }
    
    public async Task<Guid> GetHashedRecoveryEmail()
    {
        var recoveryEmail = await GetRecoveryEmail();
        OdinValidationUtils.AssertValidEmail(recoveryEmail, "Recovery email must be set to configure sharding");
        var hash = ByteArrayUtil.ReduceSHA256Hash(recoveryEmail);
        return hash;
    }
        

    public async Task UpdateAccountRecoveryEmail(Guid nonceId)
    {
        var email = await recoveryEmailer.GetNonceDataOrFail(nonceId);
        var recovery = await GetAccountRecoveryInfo();
        recovery.Email = email;
        await AccountRecoveryInfoStorage.UpsertAsync(tblKeyValue, _accountRecoveryInfoStorageId, recovery);
    }

    private async Task<AccountRecoveryInfo> GetAccountRecoveryInfo()
    {
        var recovery = await AccountRecoveryInfoStorage.GetAsync<AccountRecoveryInfo>(tblKeyValue, _accountRecoveryInfoStorageId);
        return recovery;
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

    public async Task StartUpdateRecoveryEmail(MailAddress email, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await recoveryEmailer.EnqueueVerifyNewRecoveryEmailAddress(email);
    }
}