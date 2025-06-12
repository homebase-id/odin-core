using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Dns;
#nullable enable

public static class DnsLookupClientExtensions
{
    //

    public static async Task<IDnsQueryResponse> Query(
        this ILookupClient client,
        string resolverAddressOrHostName,
        string query,
        QueryType queryType,
        DnsQueryOptions? queryOptions = null,
        CancellationToken cancellationToken = default)
    {
        var nameServer = await ResolveNameServer(resolverAddressOrHostName, cancellationToken);
        var dnsQuestion = new DnsQuestion(query, queryType);
        queryOptions ??= new DnsQueryOptions();
        var response = await client.QueryServerAsync([nameServer], dnsQuestion, queryOptions, cancellationToken);
        return response;
    }

    //

    public static async Task<IDnsQueryResponse?> Query(
        this ILookupClient client,
        IEnumerable<string> resolverAddressesOrHostNames,
        string domain,
        QueryType queryType,
        DnsQueryOptions? queryOptions = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

        var nameServerLookups = new List<Task<NameServer>>();
        foreach (var resolver in resolverAddressesOrHostNames)
        {
            nameServerLookups.Add(ResolveNameServer(resolver, linkedCts.Token));
        }
        var nameServers = await Task.WhenAll(nameServerLookups);

        var dnsQuestion = new DnsQuestion(domain, queryType);
        queryOptions ??= new DnsQueryOptions();

        var queries = new List<Task<IDnsQueryResponse>>();
        foreach (var nameServer in nameServers)
        {
            logger?.LogDebug("DNS query {domain} {type} @{address}", domain, queryType, nameServer);
            var query = client.QueryServerAsync([nameServer], dnsQuestion, queryOptions, linkedCts.Token);
            queries.Add(query);
        }

        while (queries.Count > 0 && !linkedCts.IsCancellationRequested)
        {
            var completedQuery = await Task.WhenAny(queries);
            queries.Remove(completedQuery);

            IDnsQueryResponse? response;
            try
            {
                response = await completedQuery;
                if (response.HasError)
                {
                    logger?.LogDebug("DNS response error {domain} @{address} {error}", domain, response.NameServer, response.ErrorMessage);
                    if (response.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain)
                    {
                        continue;
                    }
                }
            }
            catch (Exception e)
            {
                logger?.LogDebug("DNS exception {domain} {error}", domain, e.Message);
                continue;
            }

            if (logger?.IsEnabled(LogLevel.Trace) == true)
            {
                if (response.Authorities.Count > 0)
                {
                    logger.LogDebug("DNS authorities @{address} {response}", response.NameServer, response.Authorities);
                }
                if (response.Answers.Count > 0)
                {
                    logger.LogDebug("DNS answers @{address} {response}", response.NameServer, response.Answers);
                }
            }

            await cts.CancelAsync();
            return response;
        }

        return null;
    }

    //

    private static async Task<NameServer> ResolveNameServer(string resolverAddressOrHostName, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(resolverAddressOrHostName, out var nameServerIp))
        {
            var ips = await System.Net.Dns.GetHostAddressesAsync(resolverAddressOrHostName, cancellationToken);
            nameServerIp = ips.First();
        }
        return new NameServer(nameServerIp);
    }

}