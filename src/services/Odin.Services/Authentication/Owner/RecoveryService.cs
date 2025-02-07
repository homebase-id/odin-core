using System;
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

namespace Odin.Services.Authentication.Owner;

public class RecoveryService
{
    private static readonly Guid RecordStorageId = Guid.Parse("7fd3665e-957f-4846-a437-61c3d76fc262");
    private const string ContextKey = "3780295a-5bc6-4e0f-8334-4b5c063099c4";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    private readonly OdinConfiguration _odinConfiguration;
    private readonly TableKeyValue _tblKeyValue;

    public RecoveryService(OdinConfiguration odinConfiguration, TableKeyValue tblKeyValue)
    {
        _odinConfiguration = odinConfiguration;
        _tblKeyValue = tblKeyValue;
    }

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
        var keyRecord = await Storage.GetAsync<RecoveryKeyRecord>(_tblKeyValue, RecordStorageId);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        var keyBytes = ByteArrayUtil.GetRndByteArray(16);
        await SaveKeyAsync(keyBytes.ToSensitiveByteArray(), odinContext);
    }

    public async Task<DecryptedRecoveryKey> GetKeyAsync(IOdinContext odinContext)
    {
        var ctx = odinContext;
        ctx.Caller.AssertHasMasterKey();

        if (ctx.AuthTokenCreated == null)
        {
            throw new OdinSecurityException("Could not validate token creation date");
        }

        var recoveryKeyWaitingPeriod = TimeSpan.FromDays(14);

#if DEBUG
        recoveryKeyWaitingPeriod = TimeSpan.FromSeconds(_odinConfiguration.Development!.RecoveryKeyWaitingPeriodSeconds);
#endif

        if (UnixTimeUtc.Now() > ctx.AuthTokenCreated!.Value.AddMilliseconds((Int64)recoveryKeyWaitingPeriod.TotalMilliseconds))
        {
            throw new OdinSecurityException($"Cannot reveal token before {recoveryKeyWaitingPeriod.Days} days from creation");
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

    private async Task<RecoveryKeyRecord> GetKeyInternalAsync()
    {
        var existingKey = await Storage.GetAsync<RecoveryKeyRecord>(_tblKeyValue, RecordStorageId);
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

        await Storage.UpsertAsync(_tblKeyValue, RecordStorageId, record);
    }
}