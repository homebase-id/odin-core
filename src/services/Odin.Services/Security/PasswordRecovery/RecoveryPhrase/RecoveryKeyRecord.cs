using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class RecoveryKeyRecord
{
    public SymmetricKeyEncryptedAes MasterKeyEncryptedRecoverKey { get; set; }
    public SymmetricKeyEncryptedAes RecoveryKeyEncryptedMasterKey { get; set; }

    public UnixTimeUtc Created { get; set; }
    
    /// <summary>
    /// The datetime the user stated they stored their recovery key.
    /// </summary>
    public UnixTimeUtc? InitialRecoveryKeyViewingDate { get; set; }
    
    /// <summary>
    /// This is the next time the owner can view their recovery key
    /// </summary>
    public UnixTimeUtc? NextViewableDate { get; set; }
}

public class RequestRecoveryKeyResult
{
    public UnixTimeUtc NextViewableDate { get; set; }
}