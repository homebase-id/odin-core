using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;

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
        var nameServer = await ResolveNameServer(resolverAddressOrHostName);
        var dnsQuestion = new DnsQuestion(query, queryType);
        queryOptions ??= new DnsQueryOptions();
        var response = await client.QueryServerAsync(new[] { nameServer }, dnsQuestion, queryOptions, cancellationToken);
        return response;
    }

    //

    public static async Task<IDnsQueryResponse?> Query(
        this ILookupClient client,
        IEnumerable<string> resolverAddressesOrHostNames,
        string domain,
        QueryType queryType,
        DnsQueryOptions? queryOptions = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = new CancellationTokenSource();

        var nameServers = new List<NameServer>();
        foreach (var resolver in resolverAddressesOrHostNames)
        {
            nameServers.Add(await ResolveNameServer(resolver));
        }

        var dnsQuestion = new DnsQuestion(domain, queryType);
        queryOptions ??= new DnsQueryOptions();

        var queries = new List<Task<IDnsQueryResponse>>();
        foreach (var nameServer in nameServers)
        {
            var query = client.QueryServerAsync(new[] { nameServer }, dnsQuestion, queryOptions, cancellationToken);
            queries.Add(query);
        }

        while (queries.Count > 0 && !cts.IsCancellationRequested)
        {
            var completedQuery = await Task.WhenAny(queries);
            queries.Remove(completedQuery);

            var response = await completedQuery;
            if (response.HasError && response.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain)
            {
                continue;
            }
            cts.Cancel();
            return response;
        }

        return default;
    }

    //

    private static async Task<NameServer> ResolveNameServer(string resolverAddressOrHostName)
    {
        if (!IPAddress.TryParse(resolverAddressOrHostName, out var nameServerIp))
        {
            var ips = await System.Net.Dns.GetHostAddressesAsync(resolverAddressOrHostName);
            nameServerIp = ips.First();
        }
        return new NameServer(nameServerIp);
    }

}