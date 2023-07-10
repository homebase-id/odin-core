using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Authentication.Owner;

public class RecoveryService
{
    private readonly SingleKeyValueStorage _storage;
    private readonly GuidId _recordKey = GuidId.FromString("_recoveryKey");

    public RecoveryService(TenantSystemStorage tenantSystemStorage)
    {
        _storage = tenantSystemStorage.SingleKeyValueStorage;
    }

    public void AssertValidKey(SensitiveByteArray recoveryKey)
    {
        var existingKey = _storage.Get<RecoveryKeyRecord>(_recordKey);

        if (null == existingKey?.Key)
        {
            throw new OdinSystemException("Recovery key not configured");
        }
        
        
    }

    public void SaveKey(SensitiveByteArray recoveryKey)
    {
        //TODO: what validations are needed here?
        var record = new RecoveryKeyRecord()
        {
            Key = recoveryKey.GetKey().ToBase64(),
            Created  = UnixTimeUtc.Now()
        };
        
        _storage.Upsert(_recordKey, record);
    }
}

public class RecoveryKeyRecord
{
    public string Key { get; set; }
    public UnixTimeUtc Created { get; set; }
}