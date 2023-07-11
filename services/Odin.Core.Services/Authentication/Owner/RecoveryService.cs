using System.Threading.Tasks;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Authentication.Owner;

public class RecoveryService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly SingleKeyValueStorage _storage;
    private readonly GuidId _recordKey = GuidId.FromString("_recoveryKey");

    public RecoveryService(TenantSystemStorage tenantSystemStorage, OdinContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
        _storage = tenantSystemStorage.SingleKeyValueStorage;
    }

    /// <summary>
    /// Validates the recovery key and returns the decrypted master key, if valid.
    /// </summary>
    public void AssertValidKey(SensitiveByteArray recoveryKey, out SensitiveByteArray masterKey)
    {
        var existingKey = GetKeyInternal();
        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        masterKey = existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(ref recoveryKey);
    }

    public async Task CreateInitialKey()
    {
        var keyRecord = _storage.Get<RecoveryKeyRecord>(_recordKey);
        if (null != keyRecord)
        {
            throw new OdinSystemException("Recovery key already exists");
        }

        var key = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        this.SaveKey(key);
        await Task.CompletedTask;
    }

    public Task<byte[]> GetKey()
    {
        // _contextAccessor.GetCurrent().AuthTokenCreated?.
        //TODO: check the age of the ClientAuthToken; it must be more than XX days old
        var keyRecord = GetKeyInternal();
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
        var recoverKey = keyRecord.MasterKeyEncryptedRecoverKey.DecryptKeyClone(ref masterKey);
        return Task.FromResult(recoverKey.GetKey());
    }

    private RecoveryKeyRecord GetKeyInternal()
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(_recordKey);
        return existingKey;
    }

    private void SaveKey(SensitiveByteArray recoveryKey)
    {
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(ref masterKey, ref recoveryKey),
            Created = UnixTimeUtc.Now(),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(ref recoveryKey, ref masterKey)
        };

        _storage.Upsert(_recordKey, record);
    }
}

public class RecoveryKeyRecord
{
    public SymmetricKeyEncryptedAes MasterKeyEncryptedRecoverKey { get; set; }
    public SymmetricKeyEncryptedAes RecoveryKeyEncryptedMasterKey { get; set; }

    public UnixTimeUtc Created { get; set; }
}