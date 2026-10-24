using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirKeyRecord
{
    /// <summary>
    /// The master key, locked by the distribution key the shards rebuild.  Every deal since recovery
    /// in the browser writes this (docs/zero-knowledge-recovery-plan.md, decision 6), so server-dealt
    /// recovery never touches the recovery phrase.
    /// </summary>
    public SymmetricKeyEncryptedAes ShamirDistributionKeyEncryptedMasterKey { get; set; }

    /// <summary>
    /// Deals from before recovery in the browser: the recovery phrase, locked by the distribution
    /// key.  Read only until the phrase takeover re-deals them.
    /// </summary>
    public SymmetricKeyEncryptedAes ShamirDistributionKeyEncryptedRecoveryKey { get; set; }

    public UnixTimeUtc Created { get; set; }
}
