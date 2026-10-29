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

    /// <summary>
    /// When the browser took the phrase over (docs/zero-knowledge-recovery-plan.md, decision 3).  From
    /// then on <see cref="MasterKeyEncryptedRecoverKey"/> and <see cref="RecoveryKeyEncryptedMasterKey"/>
    /// are null: the server holds no copy it can open.
    /// </summary>
    public UnixTimeUtc? TakenOver { get; set; }

    /// <summary>The phrase's check value (<c>RecoveryPhraseCheck</c>), set at the takeover.</summary>
    public string PhraseCheck64 { get; set; }
}

public class RequestRecoveryKeyResult
{
    public UnixTimeUtc NextViewableDate { get; set; }
    public string Key { get; set; }

    /// <summary>As <see cref="RecoveryKeyResult.ZkMasterKeyEncryptedPhraseKey"/>.</summary>
    public string ZkMasterKeyEncryptedPhraseKey { get; set; }
}