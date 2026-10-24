using System.Collections.Generic;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// What recovery with friends gives the browser that started it, once enough friends approved: what
/// <c>PrepareFinalKeys</c> uses for the automated deal, and the friend deal's copies.  The browser
/// rebuilds the secret from the shards and the dealer's per-shard keys, opens the friends' private key
/// with it, and then the password.
/// </summary>
/// <remarks>
/// Sent sealed to the browser's throwaway key (<see cref="ShamirRecoveryBundleResponse"/>).  Inside it a
/// gated friend's shard is sealed to that key again: its friend's browser sealed it.
/// </remarks>
public sealed class ShamirRecoveryBundle
{
    public int MinMatchingShards { get; init; }

    /// <summary>The dealer package's envelopes: each shard's key, and its index.</summary>
    public List<DealerShardEnvelope> Envelopes { get; init; }

    /// <summary>
    /// The collected shards.  <see cref="PlayerEncryptedShard.DealerEncryptedShard"/> is the piece locked by
    /// its shard key, and, for a gated friend, sealed to the throwaway key first.
    /// </summary>
    public List<PlayerEncryptedShard> Shards { get; init; }

    public ZkShamirRecoveryCopies Copies { get; init; }
}

/// <summary><see cref="ShamirRecoveryBundle"/>'s JSON, sealed to the recovering browser's throwaway key.</summary>
public sealed class ShamirRecoveryBundleResponse
{
    public string SealedBundle64 { get; init; }
}

/// <summary>Starts recovery; a friend deal needs the recovering browser's throwaway public key.</summary>
public sealed class InitiateRecoveryModeRequest
{
    public string RecoveryPublicKeyJwk { get; init; }
}

/// <summary>
/// The email link's nonce, posted by the browser that opened it, with its throwaway public key.  For a
/// friend deal it must be the browser that started recovery.
/// </summary>
public sealed class EnterRecoveryModeRequest
{
    public string Id { get; init; }
    public string RecoveryPublicKeyJwk { get; init; }
}
