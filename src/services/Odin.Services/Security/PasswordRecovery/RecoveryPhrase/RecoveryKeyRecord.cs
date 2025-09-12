using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class RecoveryKeyRecord
{
    public SymmetricKeyEncryptedAes MasterKeyEncryptedRecoverKey { get; set; }
    public SymmetricKeyEncryptedAes RecoveryKeyEncryptedMasterKey { get; set; }

    public UnixTimeUtc Created { get; set; }
}