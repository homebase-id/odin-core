using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;

namespace Odin.Core.Dns;
#nullable enable

public class AuthoritativeDnsLookup(ILogger<AuthoritativeDnsLookup> logger, ILookupClient dnsClient)
    : IAuthoritativeDnsLookup
{
    public async Task<IAuthoritativeDnsLookupResult> LookupRootAuthority(CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Beginning look up of root servers");

        var result = new AuthoritativeDnsLookupResult();

        var response = await dnsClient.QueryAsync(".", QueryType.SOA, cancellationToken: cancellationToken);
        if (response.HasError)
        {
            throw new AuthoritativeDnsLookupException($"Error getting root servers (soa): {response.ErrorMessage}");
        }
        var soa = response.Answers.SoaRecords().First();
        result.AuthoritativeDomain = soa.DomainName.ToString()?.TrimEnd('.') ?? "";
        result.AuthoritativeNameServer = soa.MName.ToString()?.TrimEnd('.') ?? "";

        response = await dnsClient.QueryAsync(".", QueryType.NS, cancellationToken: cancellationToken);
        if (response.HasError)
        {
            throw new AuthoritativeDnsLookupException($"Error getting root servers (ns): {response.ErrorMessage}");
        }
        result.NameServers = response.Answers.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();

        return result;
    }

    //

    public async Task<IAuthoritativeDnsLookupResult> LookupDomainAuthority(string domain, CancellationToken cancellationToken = default)
    {
        var authoritatives = new AuthoritativeDnsLookupResult();

        domain = domain.Trim().Trim('.');
        logger.LogDebug("Beginning look up of authoritative records for {domain}", domain);

        var roots = await LookupRootAuthority(cancellationToken);
        if (domain == "") // looking up root?
        {
            return roots;
        }

        var nameServers = roots.NameServers;
        var dnsQueryOptions = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };

        try
        {
            // Perform iterative DNS resolution starting from the root
            var labels = domain.Split('.');
            var subdomain = "";

            for (var idx = labels.Length - 1; idx >= 0; idx--)
            {
                // Advance domain, e.g. "com" => "example.com"
                subdomain = labels[idx] + (string.IsNullOrEmpty(subdomain) ? "" : ".") + subdomain;

                nameServers = await LookUpGlue(nameServers, subdomain, dnsQueryOptions);
                if (!nameServers.Any())
                {
                    // Did not find any glue here, get out
                    break;
                }

                authoritatives.NameServers = new List<string>(nameServers);
                var (authoritativeDomain, authoritativeNameServer) = await LookUpAuthoritatives(nameServers, subdomain, dnsQueryOptions);
                if (authoritativeDomain == "" || authoritativeNameServer == "")
                {
                    // Did not find a SOA record here, get out
                    break;
                }

                authoritatives.AuthoritativeDomain = authoritativeDomain;
                authoritatives.AuthoritativeNameServer = authoritativeNameServer;
                authoritatives.NameServers = await LookUpNameServers(nameServers, authoritativeDomain, dnsQueryOptions);

            } // end for

            logger.LogDebug("Authoritative for {domain}: AuthoritativeDomain={AuthoritativeDomain}, AuthoritativeNameServer={AuthoritativeNameServer}, NameServers={NameServers}",
                domain,
                authoritatives.AuthoritativeDomain,
                authoritatives.AuthoritativeNameServer,
                string.Join(',', authoritatives.NameServers));

            return authoritatives;
        }
        catch (Exception e)
        {
            logger.LogDebug("DNS lookup failed: {error}", e.Message);
            return new AuthoritativeDnsLookupResult(e);
        }
    }

    //

    private async Task<List<string>> LookUpGlue(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions,
        CancellationToken cancellationToken = default)
    {
        var response = await dnsClient.Query(resolvers, domain, QueryType.SOA, dnsQueryOptions, logger, cancellationToken: cancellationToken);

        var result = response?.Authorities.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();
        if (result?.Count > 0)
        {
            logger.LogDebug("{resolver} found glue for {domain}: {glue}",
                response!.NameServer, domain, string.Join(',', result));
        }
        else
        {
            logger.LogDebug("{resolvers} found no glue for {domain}",
                string.Join(',', resolvers), domain);
        }

        return result ?? [];
    }

    //

    private async Task<(string authoritativeDomain, string authoritativeNameServer)> LookUpAuthoritatives(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions,
        CancellationToken cancellationToken = default)
    {
        var authoritativeDomain = "";
        var authoritativeNameServer = "";

        var response = await dnsClient.Query(resolvers, domain, QueryType.SOA, dnsQueryOptions, logger, cancellationToken: cancellationToken);
        var soa = response?.Answers.SoaRecords().FirstOrDefault();
        if (soa == null)
        {
            logger.LogDebug("{resolvers} found no SOA for {domain}", string.Join(',', resolvers), domain);
        }
        else
        {
            logger.LogDebug("{resolver} found SOA for {domain}: {soa}", response!.NameServer, domain, soa);
            authoritativeDomain = soa.DomainName.ToString()?.TrimEnd('.') ?? "";
            authoritativeNameServer = soa.MName.ToString()?.TrimEnd('.') ?? "";
        }
        return (authoritativeDomain, authoritativeNameServer);
    }

    //

    private async Task<List<string>> LookUpNameServers(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions)
    {
        var response = await dnsClient.Query(resolvers, domain, QueryType.NS, dnsQueryOptions, logger);
        var result = response?.Answers.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();
        if (result?.Count > 0)
        {
            logger.LogDebug("{resolver} found NS for {domain}: {nameservers}", response!.NameServer, domain,
                string.Join(',', result));
        }
        else
        {
            logger.LogDebug("{resolvers} found no NS for {domain}", string.Join(',', resolvers), domain);
        }
        return result ?? new List<string>();;
    }

    //

    public async Task<string> LookupZoneApex(string domain, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Beginning look up of zone apex for {domain}", domain);

        var authority = await LookupDomainAuthority(domain, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeDomain))
        {
            logger.LogDebug("LookupZoneApex did not find an authoritative server for {domain}", domain);
        }

        logger.LogDebug("Zone apex for {domain} is {authoritative}", domain, authority.AuthoritativeDomain);
        return authority.AuthoritativeDomain;
    }
}

//

public class AuthoritativeDnsLookupResult : IAuthoritativeDnsLookupResult
{
    public string AuthoritativeDomain { get; set; } = "";
    public string AuthoritativeNameServer { get; set; } = "";
    public List<string> NameServers { get; set; } = new();
    public Exception? Exception { get; set; }

    public AuthoritativeDnsLookupResult()
    {
    }

    public AuthoritativeDnsLookupResult(Exception e)
    {
        Exception = e;
    }

}

//

public class AuthoritativeDnsLookupException : OdinSystemException
{
    public AuthoritativeDnsLookupException(string message) : base(message)
    {
    }

    public AuthoritativeDnsLookupException(string message, Exception innerException) : base(message, innerException)
    {
    }
}