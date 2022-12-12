using System.Net;
using Youverse.Provisioning.Services.IdentityServer;

namespace Youverse.Provisioning.Config;

/// <summary>
/// Configuration for running this host
/// </summary>
public class ProvisioningConfiguration
{
    public ProvisioningConfiguration(IConfiguration configurationRoot)
    {
        this.Host = new HostSection(configurationRoot.GetRequiredSection("Host"));
        this.Logging = new LoggingSection(configurationRoot.GetRequiredSection("Logging"));
        this.ManagedIdentityHosts = new ManagedIdentityHostsSection(configurationRoot.GetRequiredSection("ManagedIdentityHosts"));
    }

    public HostSection Host { get; }
    
    public LoggingSection Logging { get; }
    
    public ManagedIdentityHostsSection ManagedIdentityHosts { get; }
}

public class ManagedIdentityHostsSection
{
    public ManagedIdentityHostsSection(IConfigurationSection config)
    {
        this.Servers = config.GetSection("Servers").Get<List<IdentityServerDefinition>>();
    }

    public List<IdentityServerDefinition> Servers { get; }
}
public class LoggingSection
{
    public string LogFilePath { get; }

    public LoggingSection(IConfigurationSection config)
    {
        LogFilePath = config.GetValue<string>("LogFilePath");
    }
}

public class HostSection
{
    public HostSection(IConfigurationSection hostSection)
    {
        IPAddressListenList = hostSection.GetSection("IPAddressListenList").Get<List<ListenEntry>>();
        SslCertificateRoot = hostSection.GetValue<string>("SslCertificateRoot");
    }


    /// <summary>
    /// List of IPv4 and IPv6 address on which to listen
    /// </summary>
    public List<ListenEntry> IPAddressListenList { get; }

    public string SslCertificateRoot { get; }
}

public class ListenEntry
{
    public string Ip { get; set; }
    public int HttpsPort { get; set; }
    public int HttpPort { get; set; }

    public IPAddress GetIp()
    {
        return IPAddress.Parse(this.Ip);
    }
}

