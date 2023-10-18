using System;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public class AuthorativeDnsLookup : IAuthorativeDnsLookup
{
    public string RootServer { get; set; } = "a.root-servers.net"; // https://www.iana.org/domains/root/servers
    private readonly ILogger<AuthorativeDnsLookup> _logger;

    //

    public AuthorativeDnsLookup(ILogger<AuthorativeDnsLookup> logger)
    {
        _logger = logger;
    }

    //

    public async Task<string?> Lookup(string domain)
    {
        var authoritativeServer = RootServer;

        try
        {
            // Perform iterative DNS resolution starting from the root
            var labels = domain.Split('.');
            var subdomain = "";

            for (var idx = labels.Length - 1; idx >= 0; idx--)
            {
                // Advance domain, e.g. "com" => "example.com"
                subdomain = labels[idx] + (string.IsNullOrEmpty(subdomain) ? "" : ".") + subdomain;

                _logger.LogDebug("Querying {authoritativeServer} for {domain}", authoritativeServer, subdomain);

                var dnsClient = await CreateDnsClient(authoritativeServer);

                // Get glue NS SOA record
                var response = await dnsClient.QueryAsync(subdomain, QueryType.SOA);
                if (response.HasError)
                {
                    throw new AuthorativeDnsLookupException($"DNS query failed: {response.ErrorMessage}");
                }
                var nsRecord = response.Authorities.NsRecords().FirstOrDefault();
                if (nsRecord == null)
                {
                    // Did not find a NS record here, get out
                    break;
                }

                var nsdname = nsRecord.NSDName.ToString()!;
                dnsClient = await CreateDnsClient(nsdname);

                // Get SOA record
                response = await dnsClient.QueryAsync(subdomain, QueryType.SOA);
                if (response.HasError)
                {
                    throw new AuthorativeDnsLookupException($"DNS query failed: {response.ErrorMessage}");
                }
                var soaRecord = response.Answers.SoaRecords().FirstOrDefault();
                if (soaRecord == null)
                {
                    // Did not find a SOA record here, get out
                    break;
                }
                authoritativeServer = soaRecord.MName.ToString()!;
            }

            if (authoritativeServer == RootServer)
            {
                _logger.LogDebug("No authoritative NS records found for {domain}", domain);
            }
            else
            {
                _logger.LogDebug("Authoritative DNS Server for {domain}: {authoritativeServer}",
                    domain, authoritativeServer);
            }
        }
        catch (DnsResponseException e)
        {
            throw new AuthorativeDnsLookupException(e.Message, e);
        }

        return authoritativeServer == RootServer ? null : authoritativeServer.TrimEnd('.');
    }

    //

    private static async Task<ILookupClient> CreateDnsClient(string resolverAddressOrHostName)
    {
        if (!IPAddress.TryParse(resolverAddressOrHostName, out var nameServerIp))
        {
            var ips = await System.Net.Dns.GetHostAddressesAsync(resolverAddressOrHostName);
            nameServerIp = ips.First();
        }

        var options = new LookupClientOptions(nameServerIp)
        {
            Recursion = false,
            Timeout = TimeSpan.FromSeconds(5),
            UseCache = false,
            UseTcpOnly = false,
        };

        return new LookupClient(options);
    }

}

//

public class AuthorativeDnsLookupException : OdinSystemException
{
    public AuthorativeDnsLookupException(string message) : base(message)
    {
    }

    public AuthorativeDnsLookupException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected AuthorativeDnsLookupException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}