using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public class AuthorativeDnsLookup : IAuthorativeDnsLookup
{
    private readonly ILogger<AuthorativeDnsLookup> _logger;
    private readonly ILookupClient _dnsClient;

    //

    public AuthorativeDnsLookup(ILogger<AuthorativeDnsLookup> logger, ILookupClient dnsClient)
    {
        _logger = logger;
        _dnsClient = dnsClient;
    }

    //

    public async Task<IAuthorativeDnsLookupResult> LookupRootAuthority()
    {
        _logger.LogTrace("Beginning look up of root servers");

        var result = new AuthorativeDnsLookupResult();

        var response = await _dnsClient.QueryAsync(".", QueryType.SOA);
        if (response.HasError)
        {
            throw new AuthorativeDnsLookupException($"Error getting root servers (soa): {response.ErrorMessage}");
        }
        var soa = response.Answers.SoaRecords().First();
        result.AuthorativeDomain = soa.DomainName.ToString()?.TrimEnd('.') ?? "";
        result.AuthorativeNameServer = soa.MName.ToString()?.TrimEnd('.') ?? "";

        response = await _dnsClient.QueryAsync(".", QueryType.NS);
        if (response.HasError)
        {
            throw new AuthorativeDnsLookupException($"Error getting root servers (ns): {response.ErrorMessage}");
        }
        result.NameServers = response.Answers.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();

        return result;
    }

    //

    public async Task<IAuthorativeDnsLookupResult> LookupDomainAuthority(string domain)
    {
        var authoratives = new AuthorativeDnsLookupResult();

        _logger.LogDebug("Beginning look up of authorative records for {domain}", domain);

        var roots = await LookupRootAuthority();
        if (domain.Trim('.') == "") // looking up root?
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

                authoratives.NameServers = new List<string>(nameServers);
                var (authorativeDomain, authorativeNameServer) = await LookUpAuthoratives(nameServers, subdomain, dnsQueryOptions);
                if (authorativeDomain == "" || authorativeNameServer == "")
                {
                    // Did not find a SOA record here, get out
                    break;
                }

                authoratives.AuthorativeDomain = authorativeDomain;
                authoratives.AuthorativeNameServer = authorativeNameServer;
                authoratives.NameServers = await LookUpNameServers(nameServers, authorativeDomain, dnsQueryOptions);

            } // end for

            _logger.LogDebug("Authorative for {domain}: AuthorativeDomain={AuthorativeDomain}, AuthorativeNameServer={AuthorativeNameServer}, NameServers={NameServers}",
                domain,
                authoratives.AuthorativeDomain,
                authoratives.AuthorativeNameServer,
                string.Join(',', authoratives.NameServers));

            return authoratives;
        }
        catch (Exception e)
        {
            _logger.LogDebug("DNS lookup failed: {error}", e.Message);
            return new AuthorativeDnsLookupResult(e);
        }
    }

    //

    private async Task<List<string>> LookUpGlue(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions)
    {
        var response = await _dnsClient.Query(resolvers, domain, QueryType.SOA, dnsQueryOptions, _logger);

        var result = response?.Authorities.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();
        if (result?.Count > 0)
        {
            _logger.LogDebug("{resolver} found glue for {domain}: {glue}",
                response!.NameServer, domain, string.Join(',', result));
        }
        else
        {
            _logger.LogDebug("{resolvers} found no glue for {domain}",
                string.Join(',', resolvers), domain);
        }

        return result ?? new List<string>();;
    }

    //

    private async Task<(string authorativeDomain, string authorativeNameServer)> LookUpAuthoratives(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions)
    {
        var authorativeDomain = "";
        var authorativeNameServer = "";

        var response = await _dnsClient.Query(resolvers, domain, QueryType.SOA, dnsQueryOptions, _logger);
        var soa = response?.Answers.SoaRecords().FirstOrDefault();
        if (soa == null)
        {
            _logger.LogDebug("{resolvers} found no SOA for {domain}", string.Join(',', resolvers), domain);
        }
        else
        {
            _logger.LogDebug("{resolver} found SOA for {domain}: {soa}", response!.NameServer, domain, soa);
            authorativeDomain = soa.DomainName.ToString()?.TrimEnd('.') ?? "";
            authorativeNameServer = soa.MName.ToString()?.TrimEnd('.') ?? "";
        }
        return (authorativeDomain, authorativeNameServer);
    }

    //

    private async Task<List<string>> LookUpNameServers(
        IReadOnlyCollection<string> resolvers,
        string domain,
        DnsQueryOptions dnsQueryOptions)
    {
        var response = await _dnsClient.Query(resolvers, domain, QueryType.NS, dnsQueryOptions, _logger);
        var result = response?.Answers.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.')).ToList();
        if (result?.Count > 0)
        {
            _logger.LogDebug("{resolver} found NS for {domain}: {nameservers}", response!.NameServer, domain,
                string.Join(',', result));
        }
        else
        {
            _logger.LogDebug("{resolvers} found no NS for {domain}", string.Join(',', resolvers), domain);
        }
        return result ?? new List<string>();;
    }

    //

    public async Task<string> LookupZoneApex(string domain)
    {
        _logger.LogDebug("Beginning look up of zone apex for {domain}", domain);

        var authority = await LookupDomainAuthority(domain);
        if (string.IsNullOrEmpty(authority.AuthorativeDomain))
        {
            _logger.LogDebug("LookupZoneApex did not find an authorative server for {domain}", domain);
        }

        _logger.LogDebug("Zone apex for {domain} is {authorative}", domain, authority.AuthorativeDomain);
        return authority.AuthorativeDomain;
    }
}

//

public class AuthorativeDnsLookupResult : IAuthorativeDnsLookupResult
{
    public string AuthorativeDomain { get; set; } = "";
    public string AuthorativeNameServer { get; set; } = "";
    public List<string> NameServers { get; set; } = new();
    public Exception? Exception { get; set; }

    public AuthorativeDnsLookupResult()
    {
    }

    public AuthorativeDnsLookupResult(Exception e)
    {
        Exception = e;
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
}