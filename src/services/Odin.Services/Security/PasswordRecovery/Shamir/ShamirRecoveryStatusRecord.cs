using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public sealed record ShamirRecoveryStatusRecord
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }

    /// <summary>
    /// The recovering browser's throwaway public key (P-384, a JWK): every holder's shard is sealed to
    /// it, and the server seals the recovery bundle and the slot list to it.
    /// </summary>
    public string RecoveryPublicKeyJwk { get; init; }
}


public sealed record ShamirRecoveryStatusRedacted
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
    public string Email { get; init; }

    /// <summary>
    /// Which throwaway key the recovery is for (<c>ZkSeal.KeyId</c>), pending or under way, so a browser
    /// can tell whether it is the one that started it.  Null when there is none.
    /// </summary>
    public uint? RecoveryKeyId { get; init; }

    /// <summary>While shards are collected: how many the recovery needs, and how many are in.</summary>
    public int? Needed { get; init; }

    public int? Filled { get; init; }

    /// <summary>
    /// While shards are collected: the holders and which are in (<see cref="RecoverySlot"/>, as JSON),
    /// sealed to the recovering browser's key.  The status needs no login, so the holders' names go
    /// only to the browser that started; anyone else sees the counts.
    /// </summary>
    public string SealedSlots64 { get; init; }
}

/// <summary>One holder of the recovery, and whether its shard is in.</summary>
public sealed class RecoverySlot
{
    public ShamiraPlayer Player { get; init; }
    public bool Filled { get; init; }
}

