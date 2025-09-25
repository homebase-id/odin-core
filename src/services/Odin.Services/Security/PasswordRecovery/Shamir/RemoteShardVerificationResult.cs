using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class RemoteShardVerificationResult
{
    public Dictionary<string, ShardVerificationResult> Players { get; init; } = new();
}

public class ShardVerificationResult
{
    public bool IsValid { get; set; }
    public UnixTimeUtc Created { get; init; }
    public ShardTrustLevel TrustLevel { get; set; }
}

public enum ShardTrustLevel
{
    /// <summary>
    /// Slightly stale, below medium trust.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Worrisome, medium trust level.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Active recently, highest confidence.
    /// </summary>
    High = 2,

    /// <summary>
    /// Very stale or unknown, critical concern.
    /// </summary>
    Critical = 3
}
