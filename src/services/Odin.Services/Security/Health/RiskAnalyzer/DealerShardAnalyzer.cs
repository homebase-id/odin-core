using System.Collections.Generic;
using System.Linq;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Health.RiskAnalyzer;

/// <summary>
/// GPT generated analyzer I'm exploring
/// </summary>
public static class DealerShardAnalyzer
{
    public static DealerRecoveryRiskReport Analyze(
        DealerShardPackage dealerPackage,
        PeriodicSecurityHealthCheckStatus healthStatus)
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

        // Drive this by the dealer package because it is the source of true
        foreach (var envelope in dealerPackage.Envelopes)
        {
            var playerResult = healthStatus.Players
                .FirstOrDefault(p => p.Player.OdinId == envelope.Player.OdinId);

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
                    TrustLevel = ShardTrustLevel.RedAlert,
                    IsMissing = true
                });
            }
        }

        var validCount = allResults.Count(p => !p.IsMissing && p.IsValid);
        var isRecoverable = validCount >= dealerPackage.MinMatchingShards;
        var risk = EvaluateRisk(validCount, dealerPackage.MinMatchingShards, allResults);

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

    private static RecoveryRiskLevel EvaluateRisk(
        int validCount,
        int minRequired,
        List<PlayerShardHealthResult> players)
    {
        if (validCount < minRequired)
            return RecoveryRiskLevel.Critical;

        // Enough shards, but check trust levels
        if (players.Any(p => p.IsMissing))
            return RecoveryRiskLevel.Critical; // missing shards is worst case

        if (players.Any(p => p.TrustLevel == ShardTrustLevel.RedAlert))
            return RecoveryRiskLevel.High;

        if (players.Any(p => p.TrustLevel == ShardTrustLevel.Warning))
            return RecoveryRiskLevel.High;

        if (players.Any(p => p.TrustLevel == ShardTrustLevel.TheSideEye))
            return RecoveryRiskLevel.Moderate;

        return RecoveryRiskLevel.Low;
    }
}