using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Services.Authentication.Owner;

public class RecoveryService
{
    
    private readonly SingleKeyValueStorage _storage;
    private readonly Guid _recordStorageId = Guid.Parse("7fd3665e-957f-4846-a437-61c3d76fc262");
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly OdinConfiguration _odinConfiguration;

    public RecoveryService(TenantSystemStorage tenantSystemStorage, OdinConfiguration odinConfiguration)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _odinConfiguration = odinConfiguration;

        const string k = "3780295a-5bc6-4e0f-8334-4b5c063099c4";
        Guid contextKey = Guid.Parse(k);
        _storage = tenantSystemStorage.CreateSingleKeyValueStorage(contextKey);
    }

    /// <summary>
    /// Validates the recovery key and returns the decrypted master key, if valid.
    /// </summary>
    public void AssertValidKey(string text, out SensitiveByteArray masterKey, IdentityDatabase db)
    {
        var existingKey = GetKeyInternal(db);
        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        var key = BIP39Util.DecodeBIP39(text);
        masterKey = existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(key);
    }

    public async Task CreateInitialKey(IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();
        var keyRecord = _storage.Get<RecoveryKeyRecord>(db, _recordStorageId);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        var keyBytes = ByteArrayUtil.GetRndByteArray(16);
        SaveKey(keyBytes.ToSensitiveByteArray(), odinContext, db);

        await Task.CompletedTask;
    }

    public Task<DecryptedRecoveryKey> GetKey(IOdinContext odinContext, IdentityDatabase db)
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

        var keyRecord = GetKeyInternal(db);
        var masterKey = odinContext.Caller.GetMasterKey();
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

    private RecoveryKeyRecord GetKeyInternal(IdentityDatabase db)
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(db, _recordStorageId);
        return existingKey;
    }

    private void SaveKey(SensitiveByteArray recoveryKey, IOdinContext odinContext, IdentityDatabase db)
    {
        var masterKey = odinContext.Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(masterKey, recoveryKey),
            Created = UnixTimeUtc.Now(),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(recoveryKey, masterKey)
        };

        _storage.Upsert(db, _recordStorageId, record);
    }
}