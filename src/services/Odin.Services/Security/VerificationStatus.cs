using Odin.Core.Time;

namespace Odin.Services.Security;

public class VerificationStatus
{
    public UnixTimeUtc PasswordLastVerified { get; set; }
    public UnixTimeUtc RecoveryKeyLastVerified { get; set; }
    public UnixTimeUtc DistributedRecoveryLastVerified { get; set; }
}