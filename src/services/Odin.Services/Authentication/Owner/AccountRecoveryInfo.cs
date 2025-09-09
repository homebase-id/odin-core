namespace Odin.Services.Authentication.Owner;

/// <summary>
/// Information about how this owner can recover their account in the case of losing their password
/// </summary>
public class AccountRecoveryInfo
{
    public string Email { get; set; }
}