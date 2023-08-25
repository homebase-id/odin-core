#nullable enable
namespace Odin.Hosting.Controllers.Anonymous.Home;

public class HomeAuthenticationState
{
    /// <summary>
    /// Final url in which to redirect
    /// </summary>
    public string FinalUrl { get; set; }

    /// <summary>
    /// Base-64 encoded public key used to encrypt the shared secret
    /// </summary>
    public string EccPk64 { get; set; }
}