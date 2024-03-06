#nullable enable
namespace Odin.Hosting.Controllers.Home.Auth;

public class HomeAuthenticationState
{
    /// <summary>
    /// Final url in which to redirect
    /// </summary>
    public string? FinalUrl { get; set; }
    
    /// <summary>
    /// Return url to redirect to after auth is finalized
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Base-64 encoded public key used to encrypt the shared secret
    /// </summary>
    public string? EccPk64 { get; set; }
}