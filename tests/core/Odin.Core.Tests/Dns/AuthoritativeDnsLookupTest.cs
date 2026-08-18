using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Tests.Dns;

public class AuthoritativeDnsLookupTest
{
    [Test]
    public async Task ItShouldGetTheRootServers()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose);
        var lookup = new AuthoritativeDnsLookup(logger, new LookupClient());
        var result = await lookup.LookupRootAuthorityAsync(CancellationToken.None);
        Assert.That(result.AuthoritativeDomain, Is.EqualTo(""));
        Assert.That(result.AuthoritativeNameServer, Is.EqualTo("a.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("a.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("h.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("m.root-servers.net"));
        LogEvents.AssertEvents(logStore.GetLogEvents());
    }

    [Test, Explicit]
    [TestCase("", "", "a.root-servers.net", "a.root-servers.net", 1)]
    [TestCase(".", "", "a.root-servers.net", "a.root-servers.net", 1)]
    [TestCase("com", "com", "a.gtld-servers.net", "a.gtld-servers.net", 1)]
    [TestCase("com.", "com", "a.gtld-servers.net", "a.gtld-servers.net", 1)]
    [TestCase("dk", "dk", "b.nic.dk", "b.nic.dk", 1)]
    [TestCase("dk.", "dk", "b.nic.dk", "b.nic.dk", 1)]
    [TestCase("id", "id", "b.dns.id", "b.dns.id", 1)]
    [TestCase("example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("www.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("www.example.com.", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("aslikdjaslidjsakldj.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("foo.bar.baz.www.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("sebbarg.dk", "sebbarg.dk", "ns1.sebbarg.dk", "ns1.sebbarg.dk", 1)]
    [TestCase("www.sebbarg.dk", "sebbarg.dk", "ns1.sebbarg.dk", "ns1.sebbarg.dk", 1)]
    [TestCase("seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("michael.seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("capi.michael.seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("bishwajeetparhi.dev", "bishwajeetparhi.dev", "ns1.yay.com", "ns1.yay.com", 1)]
    [TestCase("capi.bishwajeetparhi.dev", "bishwajeetparhi.dev", "ns1.yay.com", "ns1.yay.com", 1)]
    [TestCase("stefcoenen.be", "stefcoenen.be", "phil.ns.cloudflare.com", "phil.ns.cloudflare.com", 1)]
    [TestCase("www.stefcoenen.be", "stefcoenen.be", "phil.ns.cloudflare.com", "phil.ns.cloudflare.com", 1)]
    [TestCase("yagni.dk", "yagni.dk", "adele.ns.cloudflare.com", "adele.ns.cloudflare.com", 1)]
    [TestCase("www.yagni.dk", "yagni.dk", "adele.ns.cloudflare.com", "adele.ns.cloudflare.com", 1)]
    [TestCase("id.pub", "id.pub","ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("dns.id.pub", "id.pub", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("admin.dominion.id", "dominion.id", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("dominion.id", "dominion.id", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("martin.vonhaller.info", "vonhaller.info", "ns01.one.com", "ns01.one.com", 1)]
    [TestCase("wrwerakujsdjhaskdjashdaskjdhxcmvnuj.com", "com", "a.gtld-servers.net", "a.gtld-servers.net", 1)]
    [TestCase("ertertakujsdjhaskdjashdaskjdhxcmvnuj.id", "id", "b.dns.id", "b.dns.id", 1)]
    [TestCase("not a domain", "", "", "", 0)]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "", "", "", 0)]
    [TestCase("ack.ack.demo.rocks", "demo.rocks", "ns1.demo.rocks", "ns2.demo.rocks", 0)]
    // Live counterparts of the mocked empty-non-terminal tests below: real delegations
    // to ns1/ns2.id.pub, john.aage having no zone cut at aage (delegated from sebbarg.net)
    [TestCase("dingdong.sebbarg.net", "dingdong.sebbarg.net", "ns1.id.pub", "ns2.id.pub", 1)]
    [TestCase("john.aage.sebbarg.net", "john.aage.sebbarg.net", "ns1.id.pub", "ns2.id.pub", 1)]
    public async Task ItShouldLookupAuthoritativeStuff(
        string domain,
        string expectedAuthorityDomain,
        string expectedAuthorityNameserver,
        string expectedOtherNameServer,
        int expectedMinNameServers)
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose);
        var lookup = new AuthoritativeDnsLookup(logger, new LookupClient());
        var result = await lookup.LookupDomainAuthorityAsync(domain, CancellationToken.None);
        Assert.That(result.Exception, Is.Null);
        Assert.That(result.AuthoritativeDomain, Is.EqualTo(expectedAuthorityDomain));
        Assert.That(result.AuthoritativeNameServer, Is.EqualTo(expectedAuthorityNameserver));
        Assert.That(result.NameServers.Count, Is.GreaterThanOrEqualTo(expectedMinNameServers));
        Assert.That(result.NameServers, Is.Empty.Or.Contain(expectedOtherNameServer));
        LogEvents.AssertEvents(logStore.GetLogEvents());
    }

    //

    [Test, Explicit]
    [TestCase("", "")]
    [TestCase(".", "")]
    [TestCase("com", "com")]
    [TestCase("example.com", "example.com")]
    [TestCase("www.example.com", "example.com")]
    [TestCase("aslikdjaslidjsakldj.example.com", "example.com")]
    [TestCase("foo.bar.baz.www.example.com", "example.com")]
    [TestCase("yagni.dk", "yagni.dk")]
    [TestCase("www.yagni.dk", "yagni.dk")]
    [TestCase("sebbarg.net", "sebbarg.net")]
    [TestCase("foo.sebbarg.net", "sebbarg.net")]
    [TestCase("not a domain", "")]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "")]
    public async Task ItShouldLookupZoneApexForTheDomain(string domain, string expectedZoneApex)
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose);
        var lookup = new AuthoritativeDnsLookup(logger, new LookupClient());
        var result = await lookup.LookupZoneApexAsync(domain, CancellationToken.None);

        Assert.That(result, Is.EqualTo(expectedZoneApex));
        LogEvents.AssertEvents(logStore.GetLogEvents());
    }

    //
    // Data-level tests against a fully mocked DNS tree (no network, no PowerDNS).
    //
    // Simulated universe (all nameservers are IP-literals so the resolver extension
    // never touches System.Net.Dns):
    //
    //   .                        @127.0.0.1  (root)
    //   net.                     @127.0.0.2
    //   sebbarg.net.             @127.0.0.3  <- no cut at aage.sebbarg.net (empty non-terminal)
    //   john.aage.sebbarg.net.   @127.0.0.4  <- delegated directly from sebbarg.net
    //

    private const string RootNs = "127.0.0.1";
    private const string NetNs = "127.0.0.2";
    private const string SebbargNs = "127.0.0.3";
    private const string JohnNs = "127.0.0.4";

    private static Mock<ILookupClient> CreateMockedDnsTree()
    {
        var mock = new Mock<ILookupClient>();

        // Root bootstrap (recursive resolver)
        mock.Setup(c => c.QueryAsync(".", QueryType.SOA, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers: [Soa(".", "a.root-servers.net.")]));
        mock.Setup(c => c.QueryAsync(".", QueryType.NS, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers: [Ns(".", RootNs + ".")]));

        // net: delegated by the root, has SOA + NS on its own server
        SetupZone(mock, RootNs, "net.", QueryType.SOA, authorities: [Ns("net.", NetNs + ".")]);
        SetupZone(mock, NetNs, "net.", QueryType.SOA, answers: [Soa("net.", "ns.net-registry.example.")]);
        SetupZone(mock, NetNs, "net.", QueryType.NS, answers: [Ns("net.", NetNs + ".")]);

        // sebbarg.net: delegated by net
        SetupZone(mock, NetNs, "sebbarg.net.", QueryType.SOA, authorities: [Ns("sebbarg.net.", SebbargNs + ".")]);
        SetupZone(mock, SebbargNs, "sebbarg.net.", QueryType.SOA, answers: [Soa("sebbarg.net.", "ns1.sebbarg.net.")]);
        SetupZone(mock, SebbargNs, "sebbarg.net.", QueryType.NS, answers: [Ns("sebbarg.net.", SebbargNs + ".")]);

        // aage.sebbarg.net: EMPTY NON-TERMINAL - no delegation, NODATA with the zone's SOA
        // in the authority section
        SetupZone(mock, SebbargNs, "aage.sebbarg.net.", QueryType.SOA,
            authorities: [Soa("sebbarg.net.", "ns1.sebbarg.net.")]);

        // john.aage.sebbarg.net: delegated DIRECTLY from the sebbarg.net zone
        SetupZone(mock, SebbargNs, "john.aage.sebbarg.net.", QueryType.SOA,
            authorities: [Ns("john.aage.sebbarg.net.", JohnNs + ".")]);
        SetupZone(mock, JohnNs, "john.aage.sebbarg.net.", QueryType.SOA,
            answers: [Soa("john.aage.sebbarg.net.", "ns1.elsewhere.example.")]);
        SetupZone(mock, JohnNs, "john.aage.sebbarg.net.", QueryType.NS,
            answers: [Ns("john.aage.sebbarg.net.", JohnNs + ".")]);

        // Anything not set up resolves to a null response (Moq default) = "no answer",
        // which the query extension treats as a failed lookup
        return mock;
    }

    [Test]
    public async Task ItShouldFindADelegationBelowAnEmptyNonTerminal()
    {
        // Regression: the label walk used to stop at the first label without an NS
        // referral, so john.aage.sebbarg.net never got past aage.sebbarg.net and the
        // delegation directly below it was missed
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose);
        var lookup = new AuthoritativeDnsLookup(logger, CreateMockedDnsTree().Object);

        var result = await lookup.LookupDomainAuthorityAsync("john.aage.sebbarg.net", CancellationToken.None);

        Assert.That(result.Exception, Is.Null);
        Assert.That(result.AuthoritativeDomain, Is.EqualTo("john.aage.sebbarg.net"));
        Assert.That(result.AuthoritativeNameServer, Is.EqualTo("ns1.elsewhere.example"));
        Assert.That(result.NameServers, Is.EquivalentTo(new[] { JohnNs }));
    }

    [Test]
    public async Task ItShouldReturnTheEnclosingZoneForAnEmptyNonTerminal()
    {
        // aage.sebbarg.net itself has no delegation and no SOA - the authority is the
        // enclosing sebbarg.net zone
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose);
        var lookup = new AuthoritativeDnsLookup(logger, CreateMockedDnsTree().Object);

        var result = await lookup.LookupDomainAuthorityAsync("aage.sebbarg.net", CancellationToken.None);

        Assert.That(result.Exception, Is.Null);
        Assert.That(result.AuthoritativeDomain, Is.EqualTo("sebbarg.net"));
        Assert.That(result.AuthoritativeNameServer, Is.EqualTo("ns1.sebbarg.net"));
        Assert.That(result.NameServers, Is.EquivalentTo(new[] { SebbargNs }));
    }

    private static void SetupZone(
        Mock<ILookupClient> mock,
        string serverIp,
        string queryName,
        QueryType queryType,
        DnsResourceRecord[]? answers = null,
        DnsResourceRecord[]? authorities = null)
    {
        mock.Setup(c => c.QueryServerAsync(
                It.Is<IReadOnlyCollection<NameServer>>(s => s.Count == 1 && s.First().ToString()!.Contains(serverIp)),
                It.Is<DnsQuestion>(q => q.QueryName.Value == queryName && q.QuestionType == queryType),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers, authorities));
    }

    private static IDnsQueryResponse Response(
        DnsResourceRecord[]? answers = null,
        DnsResourceRecord[]? authorities = null)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers ?? []);
        mock.SetupGet(x => x.Authorities).Returns(authorities ?? []);
        mock.SetupGet(x => x.NameServer).Returns(new NameServer(IPAddress.Loopback));
        return mock.Object;
    }

    private static NsRecord Ns(string owner, string nsdName)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.NS, QueryClass.IN, 3600, 0);
        return new NsRecord(info, DnsString.Parse(nsdName));
    }

    private static SoaRecord Soa(string owner, string mName)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.SOA, QueryClass.IN, 3600, 0);
        return new SoaRecord(info, DnsString.Parse(mName), DnsString.Parse("hostmaster." + owner.TrimStart('.')), 1, 1, 1, 1, 1);
    }

    //

}

