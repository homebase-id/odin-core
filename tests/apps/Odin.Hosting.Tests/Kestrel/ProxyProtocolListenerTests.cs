#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Services.Authorization.Capi;
using Odin.Services.Tenant.Container;
using Serilog.Events;
using static Odin.Hosting.Tests.Kestrel.ProxyProtocolTestSupport;

namespace Odin.Hosting.Tests.Kestrel;

/// <summary>
/// PROXY protocol on real Kestrel. Two extra listen entries next to the scaffold's normal 8443:
/// 8444 trusts loopback (the "load balancer" in these tests), 8445 trusts only 10.0.0.0/8 so a
/// loopback peer is untrusted there. Both require a header on every connection.
/// </summary>
public class ProxyProtocolListenerTests
{
    private const int TrustedPort = 8444;
    private const int UntrustedPort = 8445;
    private static readonly IPAddress ClientA = IPAddress.Parse("203.0.113.45");
    private static readonly IPAddress Forged = IPAddress.Parse("198.51.100.7");

    private static readonly List<KeyValuePair<string, string>> Env =
    [
        .. ListenEntryEnv(1, 8081, TrustedPort, "127.0.0.0/8", "::1/128"),
        .. ListenEntryEnv(2, 8082, UntrustedPort, "10.0.0.0/8"),
    ];

    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(
            envOverrides: Env.ToDictionary(kv => kv.Key, kv => kv.Value),
            testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
        ClearEnv(Env);
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    private static string EchoUrl(string host, int port) => $"https://{host}:{port}/api/v2/health/ip";

    private static async Task<string?> EchoAsync(HttpClient client, string host = "frodo.dotyou.cloud", int port = TrustedPort)
    {
        var response = await client.GetAsync(EchoUrl(host, port));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
        return body!["ip"];
    }

    // 1. The claimed client IP is adopted
    [Test]
    public async Task ClaimedClientAddress_IsAdopted_V2()
    {
        using var client = CreateHttpClient(TrustedPort, V2Header(ClientA, 40001, IPAddress.Loopback, TrustedPort));
        Assert.That(await EchoAsync(client), Is.EqualTo(ClientA.ToString()));
    }

    [Test]
    public async Task ClaimedClientAddress_IsAdopted_V1()
    {
        var claimed = IPAddress.Parse("203.0.113.46");
        using var client = CreateHttpClient(TrustedPort, V1Header(claimed, 40002, IPAddress.Loopback, TrustedPort));
        Assert.That(await EchoAsync(client), Is.EqualTo(claimed.ToString()));
    }

    [Test]
    public async Task ClaimedIPv6ClientAddress_IsAdopted()
    {
        var claimed = IPAddress.Parse("2001:db8::45");
        using var client = CreateHttpClient(TrustedPort, V2Header(claimed, 40003, IPAddress.IPv6Loopback, TrustedPort));
        Assert.That(await EchoAsync(client), Is.EqualTo(claimed.ToString()));
    }

    // LOCAL = the balancer talking on its own behalf (health checks): keep the transport address
    [Test]
    public async Task LocalHeader_KeepsTheTransportAddress()
    {
        using var client = CreateHttpClient(TrustedPort, V2LocalHeader());
        var seen = IPAddress.Parse((await EchoAsync(client))!);
        Assert.That(IPAddress.IsLoopback(seen.IsIPv4MappedToIPv6 ? seen.MapToIPv4() : seen), Is.True, seen.ToString());
    }

    // 2. A missing header is rejected
    [Test]
    public void MissingHeader_IsRejected()
    {
        using var client = CreateHttpClient(TrustedPort, proxyHeader: null);
        var ex = Assert.CatchAsync(async () => await client.GetAsync(EchoUrl("frodo.dotyou.cloud", TrustedPort)));
        Assert.That(ex, Is.Not.Null, "a TLS ClientHello with no PROXY header must not be served");
    }

    [Test]
    public void MissingHeader_OnTheHttpPort_IsRejected()
    {
        using var client = CreateHttpClient(8081, proxyHeader: null);
        Assert.CatchAsync(async () => await client.GetAsync("http://frodo.dotyou.cloud:8081/api/v2/health/ip"));
    }

    // 3. A header from an untrusted peer is not honoured (the security test)
    [Test]
    public void HeaderFromUntrustedPeer_IsRejected()
    {
        using var client = CreateHttpClient(UntrustedPort, V2Header(Forged, 40004, IPAddress.Loopback, UntrustedPort));
        var ex = Assert.CatchAsync(async () => await client.GetAsync(EchoUrl("frodo.dotyou.cloud", UntrustedPort)));
        Assert.That(ex, Is.Not.Null, "an untrusted peer claiming a source address must be closed, never adopted");
    }

    // 5. Request logs show the real client
    [Test]
    public async Task RequestLog_ShowsTheClaimedClient()
    {
        using var client = CreateHttpClient(TrustedPort, V2Header(ClientA, 40005, IPAddress.Loopback, TrustedPort));
        await EchoAsync(client);

        var finished = _scaffold.GetLogEvents()[LogEventLevel.Information]
            .Where(e => e.MessageTemplate.Text.Contains("request finished"))
            .Where(e => e.Properties.TryGetValue("Path", out var p) && p.ToString().Contains("/health/ip"))
            .ToList();

        Assert.That(finished, Is.Not.Empty, "RequestLoggingMiddleware should have logged the echo request");
        Assert.That(finished.Select(e => e.Properties["RemoteIp"].ToString().Trim('"')),
            Has.All.EqualTo(ClientA.ToString()));
    }

    // 6. Per-tenant SNI certificate selection still works through the proxy path
    [Test]
    public async Task SniCertificateSelection_StillWorks()
    {
        var frodo = await HandshakeAsync(TrustedPort, V2Header(ClientA, 40006, IPAddress.Loopback, TrustedPort), "frodo.dotyou.cloud");
        var sam = await HandshakeAsync(TrustedPort, V2Header(ClientA, 40007, IPAddress.Loopback, TrustedPort), "sam.dotyou.cloud");

        Assert.That(DnsNames(frodo), Does.Contain("frodo.dotyou.cloud"));
        Assert.That(DnsNames(sam), Does.Contain("sam.dotyou.cloud"));
        Assert.That(frodo!.Thumbprint, Is.Not.EqualTo(sam!.Thumbprint), "each tenant must get its own certificate");
    }

    // 7. Peer/CAPI authentication still works through the proxy path
    [Test]
    public async Task PeerCapiAuthentication_StillWorks()
    {
        // Frodo (the caller) establishes a session towards Sam, exactly as OdinHttpClientFactory does.
        var multiTenant = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        var frodoScope = multiTenant.LookupTenantScope("frodo.dotyou.cloud")!;
        var session = frodoScope.Resolve<ICapiCallbackSession>();
        var sessionId = await session.EstablishSessionAsync("capi.sam.dotyou.cloud", TimeSpan.FromMinutes(10));

        using var client = CreateHttpClient(TrustedPort, V2Header(ClientA, 40008, IPAddress.Loopback, TrustedPort));
        client.DefaultRequestHeaders.Add(ICapiCallbackSession.SessionHttpHeaderName, $"frodo.dotyou.cloud~{sessionId}");

        var response = await client.GetAsync($"https://capi.sam.dotyou.cloud:{TrustedPort}/api/peer/v1/host/security/context");

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized), await response.Content.ReadAsStringAsync());
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("frodo.dotyou.cloud"));
    }

    // Regression: the ordinary listener is untouched by the feature
    [Test]
    public async Task PlainListener_StillServesWithoutHeader()
    {
        using var client = CreateHttpClient(8443, proxyHeader: null);
        var seen = IPAddress.Parse((await EchoAsync(client, port: 8443))!);
        Assert.That(IPAddress.IsLoopback(seen.IsIPv4MappedToIPv6 ? seen.MapToIPv4() : seen), Is.True);
    }

    private static IEnumerable<string> DnsNames(X509Certificate2? cert)
    {
        Assert.That(cert, Is.Not.Null);
        var names = new List<string> { cert!.GetNameInfo(X509NameType.DnsName, false) };
        foreach (var ext in cert.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            names.AddRange(ext.EnumerateDnsNames());
        }
        return names;
    }
}
