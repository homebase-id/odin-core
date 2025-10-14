using Odin.Core.Time;
using Odin.Services.Security.Health.RiskAnalyzer;

namespace Odin.Services.Security;

public class RecoveryInfo
{
    /// <summary>
    /// Indicates if there is a configuration of dealer and player information configured for this identity
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>
    /// Indicates when the configuration was last updated
    /// </summary>
    public UnixTimeUtc? ConfigurationUpdated { get; set; }

    public string Email { get; init; }
    public UnixTimeUtc? EmailLastVerified { get; set; }

    public bool UsesAutomaticRecovery { get; set; }

    public VerificationStatus Status { get; init; }
    
    public DealerRecoveryRiskReport RecoveryRisk { get; set; }
}