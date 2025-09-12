namespace Odin.Services.Security.Health.RiskAnalyzer;

public enum RecoveryRiskLevel
{
    /// <summary>
    /// Plenty of shards, mostly healthy and active.
    /// </summary>
    Low,

    /// <summary>
    /// Enough shards, but some are getting stale.
    /// </summary>
    Moderate,

    /// <summary>
    /// Just enough shards, or some show worrisome trust levels.
    /// </summary>
    High,

    /// <summary>
    /// Not enough usable shards to meet recovery threshold.
    /// </summary>
    Critical
}