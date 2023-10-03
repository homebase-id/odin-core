using System;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Authentication.Owner;

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
    public void AssertValidKey(string recoveryKey, out SensitiveByteArray masterKey)
    {
        var existingKey = GetKeyInternal();
        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        var key = recoveryKey.ToUtf8ByteArray().ToSensitiveByteArray();

        masterKey = existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(ref key);
    }

    public async Task CreateInitialKey()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        var keyRecord = _storage.Get<RecoveryKeyRecord>(_recordStorageId);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        // var key = new Guid(ByteArrayUtil.GetRndByteArray(16)).ToString("N");
        var key = RecoveryKeyGenerator.EncodeKey(ByteArrayUtil.GetRndByteArray(16));
        this.SaveKey(key);

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
        var recoverKey = keyRecord.MasterKeyEncryptedRecoverKey.DecryptKeyClone(ref masterKey);

        var rk = new DecryptedRecoveryKey
        {
            Key = recoverKey.GetKey().ToStringFromUtf8Bytes(),
            Created = keyRecord.Created
        };

        return Task.FromResult(rk);
    }

    private RecoveryKeyRecord GetKeyInternal()
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(_recordStorageId);
        return existingKey;
    }

    private void SaveKey(string key)
    {
        //TODO: need to review the type of encryption im doing here. i.e. using
        // SymmetricKeyEncryptedAes fixes my key size to 32 bytes
        var recoveryKey = key.ToUtf8ByteArray().ToSensitiveByteArray();
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(ref masterKey, ref recoveryKey),
            Created = UnixTimeUtc.Now(),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(ref recoveryKey, ref masterKey)
        };

        _storage.Upsert(_recordStorageId, record);
    }
}