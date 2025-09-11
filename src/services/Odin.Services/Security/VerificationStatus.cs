using Odin.Core.Time;

namespace Odin.Services.Security;

public class VerificationStatus
{
    public UnixTimeUtc PasswordLastVerified { get; set; }
    public UnixTimeUtc RecoveryKeyLastVerified { get; set; }
    public PeriodicSecurityHealthCheckStatus PeriodicSecurityHealthCheckStatus { get; set; }
}