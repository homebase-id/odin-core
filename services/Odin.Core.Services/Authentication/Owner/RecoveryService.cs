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
    public SensitiveByteArray AssertValidKey(SensitiveByteArray recoveryKey)
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(_recordKey);

        if (null == existingKey?.MasterKeyEncryptedRecoverKey)
        {
            throw new OdinSystemException("Recovery key not configured");
        }

        var masterKey = existingKey.RecoveryKeyEncryptedMasterKey.DecryptKeyClone(ref recoveryKey);
    }

    public void SaveKey(SensitiveByteArray recoveryKey)
    {
        //encrypt with master key
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            MasterKeyEncryptedRecoverKey = new SymmetricKeyEncryptedAes(ref masterKey, ref recoveryKey),
            RecoveryKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(ref recoveryKey, ref masterKey),
            Created = UnixTimeUtc.Now()
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