using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamirKeyRecord
{
    public SymmetricKeyEncryptedAes MasterKeyEncryptedShamirDistributionKey { get; set; }

    /// <summary>
    /// This is the recovery key from the RecoverKeyService encrypted by the ShamirDistributionKey
    /// </summary>
    public SymmetricKeyEncryptedAes ShamirDistributionKeyEncryptedRecoveryKey { get; set; }

    public UnixTimeUtc Created { get; set; }
}