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

        int validCount = allResults.Count(p =>
            !p.IsMissing &&
            p.IsValid &&
            (
                (p.Player.Type == PlayerType.Delegate && IsReachable(p.TrustLevel)) ||
                p.Player.Type == PlayerType.Automatic
            ));

        bool allAutomatic = allResults.All(p => p.Player.Type == PlayerType.Automatic);

        // Apply risk offset if all players are automatic
        int riskAdjustedValidCount = allAutomatic ? validCount + 1 : validCount;

        bool isRecoverable = validCount >= dealerPackage.MinMatchingShards;
        var risk = EvaluateRisk(riskAdjustedValidCount, dealerPackage.MinMatchingShards);

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
            return RecoveryRiskLevel.Critical;

        if (validCount == minRequired)
            return RecoveryRiskLevel.High;

        if (validCount == minRequired + 1)
            return RecoveryRiskLevel.Moderate;

        return RecoveryRiskLevel.Low;
    }

    private static bool IsReachable(ShardTrustLevel trust)
    {
        // Reachable identity = Medium or High
        return trust >= ShardTrustLevel.Medium;
    }
}
