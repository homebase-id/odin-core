using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class RemoteShardVerificationResult
{
    public bool RemoteServerError { get; init; }
    public Dictionary<string, ShardVerificationResult> Players { get; init; } = new();
}

public class ShardVerificationResult
{
    /// <summary>
    /// When true, teh remote server is not capable of verifying the
    /// shard so clients should not retry (i.e. the drive is not created, etc.)
    /// </summary>
    public bool RemoteServerError { get; init; }
    public bool IsValid { get; set; }
    public UnixTimeUtc Created { get; init; }
    public ShardTrustLevel TrustLevel { get; set; }
}

public class RemotePlayerReadinessResult
{
    public bool IsValid { get; set; }
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
