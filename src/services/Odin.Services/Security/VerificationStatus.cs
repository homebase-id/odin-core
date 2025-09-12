using Odin.Core.Time;
using Odin.Services.Security.Health;

namespace Odin.Services.Security;

public class VerificationStatus
{
    public UnixTimeUtc PasswordLastVerified { get; set; }
    public UnixTimeUtc RecoveryKeyLastVerified { get; set; }
    
}