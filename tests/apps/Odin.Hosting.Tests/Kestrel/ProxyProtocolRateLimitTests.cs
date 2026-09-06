#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using static Odin.Hosting.Tests.Kestrel.ProxyProtocolTestSupport;

namespace Odin.Hosting.Tests.Kestrel;

/// <summary>
/// The point of the whole change: behind the balancer the rate limiter must partition by the
/// client the PROXY header names, not by the balancer. Own scaffold because the limit has to be
/// low enough to trip, which the normal fixture's setup traffic would not survive.
/// </summary>
public class ProxyProtocolRateLimitTests
{
    private const int TrustedPort = 8444;
    private const int PermitPerSecond = 4;

    private static readonly List<KeyValuePair<string, string>> Env =
    [
        .. ListenEntryEnv(1, 8081, TrustedPort, "127.0.0.0/8", "::1/128"),
        new("Host__IpRateLimitEnabled", "true"),
        new("Host__IpRateLimitRequestsPerSecond", PermitPerSecond.ToString()),
    ];

    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(
            initializeIdentity: false,
            setupOwnerAccounts: false,
            envOverrides: Env.ToDictionary(kv => kv.Key, kv => kv.Value),
            testIdentities: [TestIdentities.Frodo]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
        ClearEnv(Env);
    }

    // 4. The rate limiter partitions by the REAL client
    [Test]
    public async Task RateLimiter_PartitionsByClaimedClient()
    {
        var clientA = IPAddress.Parse("203.0.113.45");
        var clientB = IPAddress.Parse("203.0.113.46");
        const string url = $"https://frodo.dotyou.cloud:8444/api/v2/health/ping";

        using var a = CreateHttpClient(TrustedPort, V2Header(clientA, 40010, IPAddress.Loopback, TrustedPort));
        using var b = CreateHttpClient(TrustedPort, V2Header(clientB, 40011, IPAddress.Loopback, TrustedPort));

        var statusesA = new List<HttpStatusCode>();
        for (var i = 0; i < PermitPerSecond * 4; i++)
        {
            statusesA.Add((await a.GetAsync(url)).StatusCode);
        }

        var statusB = (await b.GetAsync(url)).StatusCode;

        Assert.That(statusesA, Does.Contain(HttpStatusCode.TooManyRequests),
            $"client A should have been throttled; got [{string.Join(",", statusesA)}]");
        Assert.That(statusesA.First(), Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusB, Is.EqualTo(HttpStatusCode.OK),
            "client B shares the balancer's transport address with A and must be unaffected");
    }
}
