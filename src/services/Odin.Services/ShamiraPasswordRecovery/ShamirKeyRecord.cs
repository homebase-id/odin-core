using System.Collections.Generic;
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
    
    /// <summary>
    /// The list of players that hold this key.  We store this here unencrypted so
    /// we have a way to tell the owner (after email-verification) who has their keys
    /// just in case they need contact them out of band
    /// </summary>
    public List<ShamiraPlayer> Players { get; set; }
}