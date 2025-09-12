using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security.Health.RiskAnalyzer;

public class DealerRecoveryRiskReport
{
    /// <summary>
    /// True if the dealer currently has enough usable shards
    /// to meet or exceed <see cref="MinRequired"/>.
    /// </summary>
    public bool IsRecoverable { get; init; }

    /// <summary>
    /// Number of shards considered usable (IsValid && not missing && TrustLevel != RedAlert).
    /// </summary>
    public int ValidShardCount { get; init; }

    /// <summary>
    /// The minimum number of shards required for recovery.
    /// </summary>
    public int MinRequired { get; init; }

    /// <summary>
    /// Overall system risk level, based on shard health and trust.
    /// </summary>
    public RecoveryRiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Detailed per-player shard health (includes IsMissing, TrustLevel, etc).
    /// </summary>
    public List<PlayerShardHealthResult> Players { get; init; } = new();

    public UnixTimeUtc? HealthLastChecked { get; set; }
}