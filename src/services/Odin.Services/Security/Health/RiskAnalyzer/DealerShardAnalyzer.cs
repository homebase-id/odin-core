using System.Collections.Generic;
using System.Linq;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Health.RiskAnalyzer;

/// <summary>
/// Analyzer for evaluating dealer shard health and recovery risk.
/// </summary>
public static class DealerShardAnalyzer
{
    public static DealerRecoveryRiskReport Analyze(DealerShardPackage dealerPackage, PeriodicSecurityHealthCheckStatus healthStatus)
    {
        if (dealerPackage == null || healthStatus == null)
        {
            return new DealerRecoveryRiskReport
            {
                HealthLastChecked = null,
                IsRecoverable = false,
                ValidShardCount = 0,
                MinRequired = 0,
                RiskLevel = RecoveryRiskLevel.Critical,
                Players = []
            };
        }

        var allResults = new List<PlayerShardHealthResult>();

        // Drive this by the dealer package because it is the source of truth
        foreach (var envelope in dealerPackage.Envelopes)
        {
            var playerResult = healthStatus.Players.FirstOrDefault(p => p.Player.OdinId == envelope.Player.OdinId);

            if (playerResult != null)
            {
                playerResult.IsMissing = false;
                allResults.Add(playerResult);
            }
            else
            {
                allResults.Add(new PlayerShardHealthResult
                {
                    Player = envelope.Player,
                    IsValid = false,
                    ShardId = envelope.ShardId,
                    TrustLevel = ShardTrustLevel.Critical,
                    IsMissing = true
                });
            }
        }

        // Apply the "valid shard" definition:
        // Verified AND ((delegate + reachable identity) OR automatic)
        int validCount = allResults.Count(p =>
            !p.IsMissing &&
            p.IsValid &&
            (
                (p.Player.Type == PlayerType.Delegate && IsReachable(p.TrustLevel)) ||
                p.Player.Type == PlayerType.Automatic
            ));

        bool isRecoverable = validCount >= dealerPackage.MinMatchingShards;
        var risk = EvaluateRisk(validCount, dealerPackage.MinMatchingShards);

        return new DealerRecoveryRiskReport
        {
            HealthLastChecked = healthStatus.LastUpdated,
            IsRecoverable = isRecoverable,
            ValidShardCount = validCount,
            MinRequired = dealerPackage.MinMatchingShards,
            RiskLevel = risk,
            Players = allResults
        };
    }

    private static RecoveryRiskLevel EvaluateRisk(int validCount, int minRequired)
    {
        if (validCount < minRequired)
            return RecoveryRiskLevel.Critical; // Not possible

        if (validCount == minRequired)
            return RecoveryRiskLevel.High;

        if (validCount == minRequired + 1)
            return RecoveryRiskLevel.Moderate; // Medium

        return RecoveryRiskLevel.Low;
    }

    private static bool IsReachable(ShardTrustLevel trust)
    {
        // Reachable identity = Medium or High
        return trust >= ShardTrustLevel.Medium;
    }
}
