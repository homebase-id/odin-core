namespace Odin.Services.Security;

public class RecoveryInfo
{
    public string Email { get; init; }
    public VerificationStatus Status { get; init; }
}