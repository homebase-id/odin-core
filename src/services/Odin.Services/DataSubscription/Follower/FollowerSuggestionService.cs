using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Network;

#nullable enable

namespace Odin.Services.DataSubscription.Follower;

/// <summary>Someone this identity may want to follow, and which of its connections led there.</summary>
public class FollowerSuggestion
{
    public string OdinId { get; set; } = "";

    /// <summary>This identity's connections that list them as a connection.</summary>
    public List<string> ViaConnections { get; set; } = [];

    /// <summary>This identity's connections that follow them.</summary>
    public List<string> ViaFollows { get; set; } = [];

    public int Score { get; set; }
}

public class FollowerSuggestionsResult
{
    public List<FollowerSuggestion> Results { get; set; } = [];

    /// <summary>How many connections were asked, and how many answered (the rest refused, timed out or were offline).</summary>
    public int Asked { get; set; }

    public int Answered { get; set; }

    public long ComputedAt { get; set; }
}

/// <summary>
/// People you may know: what your connections share with you of their own connections and follows, combined and
/// ranked. Your server asks each connection over peer as the connection it is, so each remote's own settings decide
/// what it shares (#1611 follow-up).
/// </summary>
/// <remarks>
/// One hop: a connection answers only about itself. Each list was already yours to read on its own; this only
/// collects them. The result goes to the caller and is cached for this identity alone.
/// </remarks>
public class FollowerSuggestionService(
    ILogger<FollowerSuggestionService> logger,
    CircleNetworkService circleNetwork,
    FollowerService followerService,
    IOdinHttpClientFactory httpClientFactory,
    TenantContext tenantContext,
    ITenantLevel2Cache<FollowerSuggestionService> cache)
{
    // Bounds on the fan-out: most recent connections asked, entries read per list, calls in flight, and per call
    public const int MaxConnectionsAsked = 50;
    public const int MaxListEntries = 200;
    private const int Parallelism = 5;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<FollowerSuggestionsResult> GetSuggestionsAsync(int count, bool refresh, IOdinContext odinContext)
    {
        // An app needs all three, so it cannot use this to collect a person's friends' lists it could not read itself
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadWhoIFollow);
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var connections = (await circleNetwork.GetConnectedIdentitiesAsync(MaxConnectionsAsked, null, odinContext)).Results
            .Select(icr => icr.OdinId)
            .ToList();

        // Keyed to the connection count, so connecting someone new recomputes rather than waiting out the hour
        var cacheKey = $"follower-suggestions:{connections.Count}";
        if (!refresh)
        {
            var cached = await cache.TryGetAsync<FollowerSuggestionsResult>(cacheKey);
            if (cached.HasValue && cached.Value != null)
            {
                return Trim(cached.Value, count);
            }
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tenantContext.HostOdinId.DomainName };
        excluded.UnionWith(connections.Select(c => c.DomainName));
        excluded.UnionWith((await followerService.GetIdentitiesIFollowAsync(MaxListEntries, null, odinContext)).Results);
        excluded.UnionWith((await circleNetwork.GetBlockedProfilesAsync(MaxListEntries, null, odinContext)).Results
            .Select(icr => icr.OdinId.DomainName));

        // The tokens come from the database, so they are made one at a time; only the HTTP calls run in parallel
        var clients = new List<(OdinId odinId, IPeerNetworkListsHttpClient client)>();
        var icrKey = odinContext.PermissionsContext.GetIcrKey();
        foreach (var odinId in connections)
        {
            var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, overrideHack: true);
            if (icr?.IsConnected() != true)
            {
                continue;
            }

            var token = icr.CreateClientAuthToken(icrKey);
            clients.Add((odinId, await httpClientFactory.CreateClientUsingAccessTokenAsync<IPeerNetworkListsHttpClient>(odinId, token)));
        }

        using var gate = new SemaphoreSlim(Parallelism);
        var answers = await Task.WhenAll(clients.Select(async c =>
        {
            await gate.WaitAsync();
            try
            {
                return (c.odinId, lists: await ReadListsAsync(c.odinId, c.client));
            }
            finally
            {
                gate.Release();
            }
        }));

        var byDomain = new Dictionary<string, FollowerSuggestion>(StringComparer.OrdinalIgnoreCase);
        FollowerSuggestion Entry(string domain) =>
            byDomain.TryGetValue(domain, out var s) ? s : byDomain[domain] = new FollowerSuggestion { OdinId = domain.ToLowerInvariant() };

        var answered = 0;
        foreach (var (via, lists) in answers)
        {
            if (lists.connected == null && lists.follows == null)
            {
                continue;
            }

            answered++;
            foreach (var domain in (lists.connected ?? []).Where(d => !excluded.Contains(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Entry(domain).ViaConnections.Add(via.DomainName);
            }

            foreach (var domain in (lists.follows ?? []).Where(d => !excluded.Contains(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Entry(domain).ViaFollows.Add(via.DomainName);
            }
        }

        var result = new FollowerSuggestionsResult
        {
            Results = byDomain.Values
                .Select(s =>
                {
                    s.Score = s.ViaConnections.Count + s.ViaFollows.Count;
                    return s;
                })
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.OdinId, StringComparer.Ordinal)
                .ToList(),
            Asked = clients.Count,
            Answered = answered,
            ComputedAt = UnixTimeUtcNowMs()
        };

        await cache.SetAsync(cacheKey, result, CacheTtl);
        return Trim(result, count);
    }

    // Either list may be refused (403: not shared with connections), or the remote may be unreachable; each counts as
    // nothing from that side, never as a failure of the whole call
    private async Task<(List<string>? connected, List<string>? follows)> ReadListsAsync(OdinId odinId,
        IPeerNetworkListsHttpClient client)
    {
        using var timeout = new CancellationTokenSource(CallTimeout);
        List<string>? connected = null;
        List<string>? follows = null;
        try
        {
            var c = await client.GetConnected(MaxListEntries, null!, timeout.Token);
            if (c.IsSuccessStatusCode)
            {
                connected = c.Content?.Results?.ToList();
            }

            var f = await client.GetIdentitiesIFollow(MaxListEntries, null!, timeout.Token);
            if (f.IsSuccessStatusCode)
            {
                follows = f.Content?.Results?.ToList();
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Follower suggestions: {odinId} did not answer", odinId);
        }

        return (connected, follows);
    }

    private static FollowerSuggestionsResult Trim(FollowerSuggestionsResult r, int count) => new()
    {
        Results = r.Results.Take(Math.Clamp(count, 1, 200)).ToList(),
        Asked = r.Asked,
        Answered = r.Answered,
        ComputedAt = r.ComputedAt
    };

    private static long UnixTimeUtcNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
