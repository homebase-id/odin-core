using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns.Health;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Dns.Health;

#nullable enable

public class DnsHealthServiceTest
{
    private static readonly AsciiDomainName Domain = new("frodo.example.com");

    // RFC 4034 section 5.4 example key (key tag 60485, algorithm 5)
    private const string Rfc4034PublicKeyBase64 =
        "AQOeiiR0GOMYkDshWoSKz9XzfwJr1AYtsmx3TGkJaNXVbfi/2pHm822aJ5iI9BMzNXxeYCmZDRD99WYwYqUSdjMmmAphXdvx" +
        "egXd/M5+X7OrzKBaMbCVdFLUUh6DhweJBjEVv5f2wwjM9XzcnOf+EPbtG9DMBmADjFDc2w/rljwvFw==";

    private static DnsKeyRecord TestKey(string owner = "frodo.example.com.", int flags = 257)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.DNSKEY, QueryClass.IN, 3600, 0);
        return new DnsKeyRecord(info, flags, 3, 5, System.Convert.FromBase64String(Rfc4034PublicKeyBase64));
    }

    private readonly Mock<IAuthoritativeDnsLookup> _authoritativeDnsLookup = new();
    private readonly Mock<IDnssecLookup> _dnssecLookup = new();
    private readonly Mock<IDnsLookupService> _dnsLookupService = new();
    private readonly Mock<ILookupClient> _dnsClient = new();

    private DnsHealthService CreateService()
    {
        var configuration = new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = new DnsConfigurationSet("131.164.170.62", "identity-host.example"),
            }
        };
        return new DnsHealthService(
            new Mock<ILogger<DnsHealthService>>().Object,
            configuration,
            _dnsClient.Object,
            _authoritativeDnsLookup.Object,
            _dnssecLookup.Object,
            _dnsLookupService.Object);
    }

    //
    // Verdict (pure)
    //

    [Test]
    public void ItShouldRankParentUnsignedAboveEverything()
    {
        Assert.That(DnsHealthService.ComputeVerdict(parentZoneSigned: false, parentDsCount: 0, anyDsMatches: false),
            Is.EqualTo(DnsHealthDnssecStatus.ParentUnsigned));
        // Even with a published (inert) DS
        Assert.That(DnsHealthService.ComputeVerdict(parentZoneSigned: false, parentDsCount: 1, anyDsMatches: true),
            Is.EqualTo(DnsHealthDnssecStatus.ParentUnsigned));
    }

    [Test]
    public void ItShouldDistinguishMissingMatchingAndMismatchingDs()
    {
        Assert.That(DnsHealthService.ComputeVerdict(true, 0, false), Is.EqualTo(DnsHealthDnssecStatus.DsMissing));
        Assert.That(DnsHealthService.ComputeVerdict(true, 1, true), Is.EqualTo(DnsHealthDnssecStatus.Secure));
        Assert.That(DnsHealthService.ComputeVerdict(true, 1, false), Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
    }

    //
    // DS matching against zone keys (pure)
    //

    [Test]
    public void ItShouldMatchAPublishedDsInWhateverDigestTypeTheParentChose()
    {
        var key = TestKey("dskey.example.com.");

        // Parent published SHA-1 (type 1) - matching must recompute in THAT type, not
        // insist on our SHA-256 default
        var publishedSha1 = DnssecLookup.ComputeDsFromDnsKey("dskey.example.com", key, 1);
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [publishedSha1]), Is.True);

        var publishedSha256 = DnssecLookup.ComputeDsFromDnsKey("dskey.example.com", key);
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [publishedSha256]), Is.True);
    }

    [Test]
    public void ItShouldNotMatchAForeignOrUncomputableDs()
    {
        var key = TestKey("dskey.example.com.");

        var foreign = new DsRecordData(11111, 13, 2, "deadbeef");
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [foreign]), Is.False);

        // Digest type we cannot compute (GOST=3) is skipped, not an exception
        var gost = new DsRecordData(60485, 5, 3, "deadbeef");
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [gost]), Is.False);
    }

    //
    // DNSSEC orchestration (mocked seams)
    //

    [Test]
    public async Task ItShouldReportInheritedWhenTheDomainIsNotAZoneCut()
    {
        // Managed-domain case: frodo.example.com lives inside the example.com zone
        _dnssecLookup.Invocations.Clear();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("example.com");

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.Inherited));
        Assert.That(result.EnclosingZone, Is.EqualTo("example.com"));
        // Inherited short-circuits: no key/DS lookups happen at all
        _dnssecLookup.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ItShouldReportZoneUnsignedWhenNoDnsKeysAreServed()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.ZoneUnsigned));
    }

    [Test]
    public async Task ItShouldOfferComputedDsRecordsWhenOneIsMissing()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMissing));
        // No CDS -> computed from the zone's public key, SHA-256
        var expected = DnssecLookup.ComputeDsFromDnsKey(Domain.DomainName, key);
        Assert.That(result.DsToPublish.Single(), Is.EqualTo(expected));
        Assert.That(result.ParentZoneSigned, Is.True);
    }

    [Test]
    public async Task ItShouldPreferPublishedCdsOverComputation()
    {
        var key = TestKey();
        var cds = new DsRecordData(60485, 5, 2, "aabbccdd");
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cds]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.DsToPublish.Single(), Is.EqualTo(cds));
    }

    [Test]
    public async Task ItShouldReportDsMismatchForAStaleParentDs()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DsRecordData(9999, 13, 2, "deadbeef")]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
        Assert.That(result.ParentDsRecords.Single().KeyTag, Is.EqualTo(9999));
    }

    //
    // Optional www (mocked lookups)
    //

    private void SetupWwwLookups(CNameRecord[]? cnames = null, ARecord[]? aRecords = null)
    {
        var authority = new AuthoritativeDnsLookupResult
        {
            AuthoritativeDomain = Domain.DomainName,
            AuthoritativeNameServer = "ns1.example",
            NameServers = ["127.0.0.9"],
        };
        _authoritativeDnsLookup
            .Setup(x => x.LookupDomainAuthorityAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authority);

        _dnsClient
            .Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.Is<DnsQuestion>(q => q.QuestionType == QueryType.CNAME),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(cnames ?? []));
        _dnsClient
            .Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.Is<DnsQuestion>(q => q.QuestionType == QueryType.A),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(aRecords ?? []));
    }

    private static IDnsQueryResponse Response(DnsResourceRecord[] answers)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers);
        mock.SetupGet(x => x.Authorities).Returns([]);
        mock.SetupGet(x => x.NameServer).Returns(new NameServer(System.Net.IPAddress.Loopback));
        return mock.Object;
    }

    private static CNameRecord Cname(string owner, string target)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.CNAME, QueryClass.IN, 3600, 0);
        return new CNameRecord(info, DnsString.Parse(target));
    }

    private static ARecord A(string owner, string ip)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.A, QueryClass.IN, 3600, 0);
        return new ARecord(info, System.Net.IPAddress.Parse(ip));
    }

    [Test]
    public async Task ItShouldReportWwwNotSetAsAnUnremarkableState()
    {
        SetupWwwLookups();

        var result = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);

        Assert.That(result.Single().Status, Is.EqualTo(OptionalRecordStatus.NotSet));
        Assert.That(result.Single().Domain, Is.EqualTo("www.frodo.example.com"));
    }

    [Test]
    public async Task ItShouldAcceptWwwPointingAtTheIdentityByCnameOrARecord()
    {
        // CNAME to the domain itself
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "frodo.example.com.")]);
        var byDomainCname = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byDomainCname.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));

        // CNAME to the same alias the apex uses
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "identity-host.example.")]);
        var byAliasCname = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byAliasCname.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));

        // A record matching the apex A
        SetupWwwLookups(aRecords: [A("www.frodo.example.com.", "131.164.170.62")]);
        var byARecord = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byARecord.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));
    }

    [Test]
    public async Task ItShouldReportADeliberateSeparateWwwSiteAsPointsElsewhere()
    {
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "some-other-site.example.")]);

        var result = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);

        Assert.That(result.Single().Status, Is.EqualTo(OptionalRecordStatus.PointsElsewhere));
        Assert.That(result.Single().Found, Is.EquivalentTo(new[] { "some-other-site.example" }));
    }

    //
    // Security-email attention rule (docs/owner-console-dnssec-panel-plan.md section 3b)
    //

    private static DnssecHealthResult Health(DnsHealthDnssecStatus status, bool parentSigned = true)
    {
        return new DnssecHealthResult { Status = status, ParentZoneSigned = parentSigned };
    }

    [Test]
    public void ItShouldFlagOnlyUserActionableDnssecStates()
    {
        // DsMismatch always: the SERVFAIL case
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.DsMismatch)), Is.True);
        // DsMissing only when the parent is signed (one actionable record away)
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.DsMissing)), Is.True);
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.DsMissing, parentSigned: false)), Is.False);

        // Everything else: no monthly nagging about states the user cannot act on
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.Secure)), Is.False);
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.Inherited)), Is.False);
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.ParentUnsigned, parentSigned: false)), Is.False);
        Assert.That(DnsHealthService.NeedsUserAttention(Health(DnsHealthDnssecStatus.ZoneUnsigned)), Is.False);
    }

    [Test]
    public async Task ItShouldReturnNullAttentionOnLookupFailure()
    {
        // A DNS hiccup must neither block the health report nor count as attention
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("dns exploded"));

        var result = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ItShouldReturnAttentionForAStaleDsAndStayQuietWhenSecure()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Stale DS -> attention with the expected DS values on board
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DsRecordData(9999, 13, 2, "deadbeef")]);
        var mismatch = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);
        Assert.That(mismatch, Is.Not.Null);
        Assert.That(mismatch!.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
        Assert.That(mismatch.DsToPublish, Is.Not.Empty);

        // Matching DS -> quiet
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([DnssecLookup.ComputeDsFromDnsKey(Domain.DomainName, key)]);
        var secure = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);
        Assert.That(secure, Is.Null);
    }
}
