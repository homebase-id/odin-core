namespace Youverse.Provisioning.Services.IdentityServer;

public class IdentityServerDefinition
{
    /// <summary>
    /// Endpoint for the identity server that manages the <see cref="ManagedDomains"/>
    /// </summary>
    public string IdentityHostName { get; set; } = "";

    public int IdentityHostPort { get; set; } = 0;

    /// <summary>
    /// List of the domains managed by this identity server 
    /// </summary>
    public List<string> ManagedDomains { get; set; } = new();
}