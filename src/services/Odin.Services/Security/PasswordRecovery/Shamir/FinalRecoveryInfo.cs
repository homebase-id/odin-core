namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class FinalRecoveryInfo
{
    public byte[] Iv { get; init; }
    public byte[] Cipher { get; init; }
}