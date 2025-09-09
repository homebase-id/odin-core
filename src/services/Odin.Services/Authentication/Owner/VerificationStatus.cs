using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class VerificationStatus
{
    public UnixTimeUtc PasswordLastVerified { get; set; }
    public UnixTimeUtc RecoveryKeyLastVerified { get; set; }
    public UnixTimeUtc DistributedRecoveryLastVerified { get; set; }
}

public class RecoveryInfo
{
    public string Email { get; init; }
    public VerificationStatus Status { get; init; }
}