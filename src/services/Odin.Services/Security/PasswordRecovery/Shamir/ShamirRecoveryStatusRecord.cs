using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirRecoveryStatusRecord
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }

    /// <summary>
    /// The recovering browser's throwaway public key (P-384, a JWK), for a friend deal: the friends
    /// seal their shards to it, and the server seals the recovery bundle to it.
    /// </summary>
    public string RecoveryPublicKeyJwk { get; init; }
}


public class ShamirRecoveryStatusRedacted
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
    public string Email { get; init; }
}

