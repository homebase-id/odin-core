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
    /// Active recently
    /// </summary>
    Thumbsup,

    /// <summary>
    /// Slightly stale
    /// </summary>
    TheSideEye,

    /// <summary>
    /// Worrisome
    /// </summary>
    Warning,

    /// <summary>
    /// Very stale or unknown
    /// https://www.youtube.com/watch?v=MGQ_Lifd-Wg (boots and cats boots and cats)
    /// </summary>
    RedAlert
}