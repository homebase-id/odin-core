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

    /// <summary>
    /// The shards predate the current password and could not be rotated; until the owner reconfigures
    /// recovery, delegates still connected can release the shards dealt before the change (#1861, #1885).
    /// </summary>
    public bool RotationPending { get; set; }


    public bool HasRecoveryKeyBeenViewed { get; set; }
}