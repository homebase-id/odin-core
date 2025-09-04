using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class VerificationStatus
{
    public UnixTimeUtc PasswordLastVerified { get; set; }
    public UnixTimeUtc RecoveryKeyLastVerified { get; set; }
}