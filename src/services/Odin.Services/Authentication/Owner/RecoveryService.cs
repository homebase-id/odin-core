using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Services.Authentication.Owner;

public class RecoveryService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly SingleKeyValueStorage _storage;
    private readonly Guid _recordStorageId = Guid.Parse("7fd3665e-957f-4846-a437-61c3d76fc262");
    private readonly OdinConfiguration _odinConfiguration;

    public RecoveryService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor, OdinConfiguration odinConfiguration)
    {
        _contextAccessor = contextAccessor;
        _odinConfiguration = odinConfiguration;

        const string k = "3780295a-5bc6-4e0f-8334-4b5c063099c4";
        Guid contextKey = Guid.Parse(k);
        _storage = tenantSystemStorage.CreateSingleKeyValueStorage(contextKey);
    }

    /// <summary>
    /// Validates the recovery key and returns the decrypted master key, if valid.
    /// </summary>
    public void AssertValidKey(string text, out SensitiveByteArray masterKey)
    {
        var existingKey = GetKeyInternal();
        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        var key = BIP39Util.DecodeBIP39(text);
        masterKey = existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(key);
    }

    public async Task CreateInitialKey()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        var keyRecord = _storage.Get<RecoveryKeyRecord>(_recordStorageId);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        var keyBytes = ByteArrayUtil.GetRndByteArray(16);
        SaveKey(keyBytes.ToSensitiveByteArray());

        await Task.CompletedTask;
    }

    public Task<DecryptedRecoveryKey> GetKey()
    {
        var ctx = _contextAccessor.GetCurrent();
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

        var keyRecord = GetKeyInternal();
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
        var recoverKey = keyRecord.MasterKeyEncryptedRecoverKey.DecryptKeyClone(masterKey);

        var readableText = BIP39Util.GenerateBIP39(recoverKey.GetKey());

        var rk = new DecryptedRecoveryKey
        {
            Key = readableText,
            Created = keyRecord.Created
        };

        recoverKey.Wipe();
        return Task.FromResult(rk);
    }

    private RecoveryKeyRecord GetKeyInternal()
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(_recordStorageId);
        return existingKey;
    }

    private void SaveKey(SensitiveByteArray recoveryKey)
    {
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(masterKey, recoveryKey),
            Created = UnixTimeUtc.Now(),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(recoveryKey, masterKey)
        };

        _storage.Upsert(_recordStorageId, record);
    }
}