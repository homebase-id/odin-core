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
#nullable enable

public class DnssecLookupTest
{
    //
    // Data-level tests against a fully mocked DNS tree (no network).
    //
    // Simulated universe (same layout as AuthoritativeDnsLookupTest):
    //
    //   .                        @127.0.0.1  (root)
    //   net.                     @127.0.0.2
    //   sebbarg.net.             @127.0.0.3  signed; publishes a DS for dingdong.sebbarg.net
    //

    private const string RootNs = "127.0.0.1";
    private const string NetNs = "127.0.0.2";
    private const string SebbargNs = "127.0.0.3";

    private static readonly byte[] Digest =
        [0xC8, 0xF8, 0x16, 0xA7, 0xA5, 0x75, 0xBD, 0xB2, 0xF9, 0x97, 0xF6, 0x82, 0xAA, 0xB2, 0x65, 0x3B];

    private static Mock<ILookupClient> CreateMockedDnsTree(bool parentSigned = true, bool dsPublished = true)
    {
        var mock = new Mock<ILookupClient>();

        // Root bootstrap (recursive resolver)
        mock.Setup(c => c.QueryAsync(".", QueryType.SOA, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers: [Soa(".", "a.root-servers.net.")]));
        mock.Setup(c => c.QueryAsync(".", QueryType.NS, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers: [Ns(".", RootNs + ".")]));

        // net: delegated by the root
        SetupQuery(mock, RootNs, "net.", QueryType.SOA, authorities: [Ns("net.", NetNs + ".")]);
        SetupQuery(mock, NetNs, "net.", QueryType.SOA, answers: [Soa("net.", "ns.net-registry.example.")]);
        SetupQuery(mock, NetNs, "net.", QueryType.NS, answers: [Ns("net.", NetNs + ".")]);

        // sebbarg.net: delegated by net
        SetupQuery(mock, NetNs, "sebbarg.net.", QueryType.SOA, authorities: [Ns("sebbarg.net.", SebbargNs + ".")]);
        SetupQuery(mock, SebbargNs, "sebbarg.net.", QueryType.SOA, answers: [Soa("sebbarg.net.", "ns1.sebbarg.net.")]);
        SetupQuery(mock, SebbargNs, "sebbarg.net.", QueryType.NS, answers: [Ns("sebbarg.net.", SebbargNs + ".")]);

        // The parent's signing state: DNSKEY present or NODATA
        SetupQuery(mock, SebbargNs, "sebbarg.net.", QueryType.DNSKEY,
            answers: parentSigned ? [DnsKey("sebbarg.net.")] : null,
            authorities: parentSigned ? null : [Soa("sebbarg.net.", "ns1.sebbarg.net.")]);

        // The DS for the delegated child, published (or not) in the parent zone
        SetupQuery(mock, SebbargNs, "dingdong.sebbarg.net.", QueryType.DS,
            answers: dsPublished ? [Ds("dingdong.sebbarg.net.", 46082, 13, 2, Digest)] : null,
            authorities: dsPublished ? null : [Soa("sebbarg.net.", "ns1.sebbarg.net.")]);

        return mock;
    }

    private static DnssecLookup CreateDnssecLookup(Mock<ILookupClient> dnsTree)
    {
        var logStore = new LogEventMemoryStore();
        var authoritative = new AuthoritativeDnsLookup(
            TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose), dnsTree.Object);
        return new DnssecLookup(
            TestLogFactory.CreateConsoleLogger<DnssecLookup>(logStore, LogEventLevel.Verbose), dnsTree.Object, authoritative);
    }

    //

    [Test]
    public async Task ItShouldReadTheDsPublishedAtTheParent()
    {
        var lookup = CreateDnssecLookup(CreateMockedDnsTree());

        var dsRecords = await lookup.GetParentDsRecordsAsync("dingdong.sebbarg.net", CancellationToken.None);

        Assert.That(dsRecords.Count, Is.EqualTo(1));
        Assert.That(dsRecords[0].KeyTag, Is.EqualTo(46082));
        Assert.That(dsRecords[0].Algorithm, Is.EqualTo(13));
        Assert.That(dsRecords[0].DigestType, Is.EqualTo(2));
        // Normalized to lowercase hex
        Assert.That(dsRecords[0].Digest, Is.EqualTo("c8f816a7a575bdb2f997f682aab2653b"));
    }

    [Test]
    public async Task ItShouldReturnEmptyWhenTheParentPublishesNoDs()
    {
        var lookup = CreateDnssecLookup(CreateMockedDnsTree(dsPublished: false));

        var dsRecords = await lookup.GetParentDsRecordsAsync("dingdong.sebbarg.net", CancellationToken.None);

        Assert.That(dsRecords, Is.Empty);
    }

    [Test]
    public async Task ItShouldDetectWhetherAZoneIsSigned()
    {
        var signed = CreateDnssecLookup(CreateMockedDnsTree(parentSigned: true));
        Assert.That(await signed.IsZoneSignedAsync("sebbarg.net", CancellationToken.None), Is.True);

        var unsigned = CreateDnssecLookup(CreateMockedDnsTree(parentSigned: false));
        Assert.That(await unsigned.IsZoneSignedAsync("sebbarg.net", CancellationToken.None), Is.False);
    }

    //
    // Live lookups (network); expectations depend on the outside world
    //

    [Test, Explicit]
    public async Task ItShouldReadLiveDnssecStateOfLongSignedDomains()
    {
        var logStore = new LogEventMemoryStore();
        var authoritative = new AuthoritativeDnsLookup(
            TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(logStore, LogEventLevel.Verbose), new LookupClient());
        var lookup = new DnssecLookup(
            TestLogFactory.CreateConsoleLogger<DnssecLookup>(logStore, LogEventLevel.Verbose), new LookupClient(), authoritative);

        // internetsociety.org has been DNSSEC-signed for over a decade
        Assert.That(await lookup.IsZoneSignedAsync("internetsociety.org"), Is.True);
        var ds = await lookup.GetParentDsRecordsAsync("internetsociety.org");
        Assert.That(ds, Is.Not.Empty);

        // gabriel.ninja: our PowerDNS signs the zone; the parent (.ninja) is signed.
        // The DS assertions may flip once the owner publishes the DS at the registrar.
        Assert.That(await lookup.IsZoneSignedAsync("ninja"), Is.True);
    }

    //
    // Mock plumbing (pattern shared with AuthoritativeDnsLookupTest)
    //

    private static void SetupQuery(
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

    private static DsRecord Ds(string owner, int keyTag, byte algorithm, byte digestType, byte[] digest)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.DS, QueryClass.IN, 3600, 0);
        return new DsRecord(info, keyTag, algorithm, digestType, digest);
    }

    private static DnsKeyRecord DnsKey(string owner)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.DNSKEY, QueryClass.IN, 3600, 0);
        return new DnsKeyRecord(info, 257, 3, 13, [0x01, 0x02, 0x03]);
    }
}
