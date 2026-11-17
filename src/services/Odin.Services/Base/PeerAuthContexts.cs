namespace Odin.Services.Base;

/// <summary>
/// The <see cref="IOdinContext.AuthContext"/> of another identity's server calling this one; Hosting's authentication
/// schemes of the same name (<c>PeerAuthConstants</c>) are these
/// </summary>
public static class PeerAuthContexts
{
    /// <summary>Another identity's server over Transit</summary>
    public const string TransitCapi = "TransitCapiAuthScheme";

    /// <summary>Another identity's server authenticated by Hosting's automated-identity scheme</summary>
    public const string AutomatedIdentity = "AutomatedIdentityAuthScheme";
}
