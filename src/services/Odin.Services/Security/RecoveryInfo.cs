using Odin.Services.Security.Health.RiskAnalyzer;

namespace Odin.Services.Security;

public class RecoveryInfo
{
    public string Email { get; init; }
    public VerificationStatus Status { get; init; }
    public DealerRecoveryRiskReport RecoveryRisk { get; set; }
}