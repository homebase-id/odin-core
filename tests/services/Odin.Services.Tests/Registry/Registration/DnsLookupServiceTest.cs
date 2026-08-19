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
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

public class DnsLookupServiceTest
{
    private static DnsLookupService CreateDnsLookupService(OdinConfiguration configuration)
    {
        var authoritativeDnsLookup = new AuthoritativeDnsLookup(
            new Mock<ILogger<AuthoritativeDnsLookup>>().Object, new LookupClient());
        var dnssecLookup = new DnssecLookup(
            new Mock<ILogger<DnssecLookup>>().Object, new LookupClient(), authoritativeDnsLookup);
        return new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, new LookupClient(), authoritativeDnsLookup,
            dnssecLookup);
    }

    private static OdinConfiguration ConfigurationWithDns(
        DnsConfigurationSet dnsConfigurationSet,
        string powerDnsApiKey = "top-secret",
        OdinConfiguration.EmailSection email = null)
    {
        return new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = dnsConfigurationSet,
                PowerDnsApiKey = powerDnsApiKey,
                ManagedDomainApexes = [new() { Apex = "demo.rocks", PrefixLabels = ["First", "Surname"] }],
                DnsResolvers = ["1.1.1.1"],
            },
            Email = email ?? new OdinConfiguration.EmailSection(),
        };
    }

    private static OdinConfiguration.EmailSection TenantMailEnabled()
    {
        return new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
            TenantMail = new OdinConfiguration.TenantMailSection
            {
                Enabled = true,
                MxNodes = ["node-a.id.pub", "node-b.id.pub"],
                SpfIncludeTarget = "_spf.id.pub",
                DmarcReportEmail = "dmarc-reports@id.pub",
                TlsReportEmail = "tls-reports@id.pub",
            }
        };
    }

    //

    [Test]
    public void ItShouldNotIncludeNsRecordsWhenNoNameServersAreConfigured()
    {
        var configuration = ConfigurationWithDns(new DnsConfigurationSet("131.164.170.62", "identity-host.example"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.example.com"));

        Assert.That(dnsConfig.Any(x => x.Type == "NS"), Is.False);
        Assert.That(dnsConfig.Count, Is.EqualTo(4)); // A, ALIAS, capi CNAME, file CNAME - unchanged
    }

    //

    [Test]
    public void ItShouldNotIncludeNsRecordsWhenPowerDnsIsNotConfigured()
    {
        // Self-hosted case: NameServers has a default value pointing at our infrastructure,
        // but without PowerDNS this deployment can never host the zone - so no NS instructions
        var configuration = ConfigurationWithDns(
            new DnsConfigurationSet(
                "131.164.170.62", "identity-host.example", ["ns1.example", "ns2.example"], "admin@example.com"),
            powerDnsApiKey: "");
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.example.com"));

        Assert.That(dnsConfig.Any(x => x.Type == "NS"), Is.False);
    }

    //

    [Test]
    public void ItShouldNotIncludeNsRecordsForManagedDomains()
    {
        // Managed domains live as records inside the shared apex zone; delegating one
        // anywhere is never valid, so no NS instructions for them
        var configuration = ConfigurationWithDns(new DnsConfigurationSet(
            "131.164.170.62", "identity-host.example",
            ["ns1.example", "ns2.example"], "admin@example.com"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.baggins.demo.rocks"));

        Assert.That(dnsConfig.Any(x => x.Type == "NS"), Is.False);
    }

    //

    [Test]
    public void ItShouldIncludeOneNsRecordPerConfiguredNameServer()
    {
        var configuration = ConfigurationWithDns(new DnsConfigurationSet(
            "131.164.170.62", "identity-host.example",
            ["NS1.Example.", "ns2.example"], "admin@example.com"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.example.com"));

        var nsRecords = dnsConfig.Where(x => x.Type == "NS").ToList();
        Assert.That(nsRecords.Count, Is.EqualTo(2));
        // Values are normalized: lowercased, no trailing dot
        Assert.That(nsRecords[0].Value, Is.EqualTo("ns1.example"));
        Assert.That(nsRecords[1].Value, Is.EqualTo("ns2.example"));
        Assert.That(nsRecords.TrueForAll(x => x.Name == ""), Is.True);
        Assert.That(nsRecords.TrueForAll(x => x.Domain == "frodo.example.com"), Is.True);
    }

    //

    private static DnsConfig Record(string type, DnsLookupRecordStatus status)
    {
        return new DnsConfig { Type = type, Status = status };
    }

    [Test]
    public void ItShouldSucceedOnVerifiedDelegationAlone()
    {
        // Verified delegation (at the PARENT - the strict all-ours check) counts as
        // success before the zone exists on our servers: the zone is created and
        // populated at the commit points that consume this verdict (Provision click,
        // create-identity ensure-net, CLI backfill), so records exist before anything
        // needs them. This is what lets the Provision button enable before the
        // Provision click creates the zone.
        var delegatedNoZoneYet = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("ALIAS", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("CNAME", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("NS", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.Success),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(delegatedNoZoneYet), Is.True);
    }

    [Test]
    public void ItShouldFallBackToTheRecordRuleWhenDelegationIsIncomplete()
    {
        // Records all good, NS incomplete (manual-records user) -> success via record rule
        var manualUser = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("NS", DnsLookupRecordStatus.DomainOrRecordNotFound),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(manualUser), Is.True);

        // Partial delegation (one NS ours, one stale/missing) must NOT count as delegated;
        // the record rule decides
        var partialDelegationBrokenRecords = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.IncorrectValue),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(partialDelegationBrokenRecords), Is.False);

        var nothingWorks = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("NS", DnsLookupRecordStatus.DomainOrRecordNotFound),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(nothingWorks), Is.False);
    }

    [Test]
    public void ItShouldKeepLegacyRuleWhenNoNsRecordsArePresent()
    {
        var aliasOnly = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("ALIAS", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(aliasOnly), Is.True);

        var brokenCname = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.IncorrectValue),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(brokenCname), Is.False);
    }

    //
    // Email records (docs/email-dns-plan.md)
    //

    [Test]
    public void ItShouldNotEmitEmailRecordsWhenTenantMailIsDisabled()
    {
        // The default config must yield a record set byte-identical to the pre-email era
        var configuration = ConfigurationWithDns(new DnsConfigurationSet("131.164.170.62", "identity-host.example"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.example.com"));

        Assert.That(dnsConfig.Count, Is.EqualTo(4)); // A, ALIAS, capi CNAME, file CNAME
        Assert.That(dnsConfig.Any(x => x.Optional), Is.False);
        Assert.That(dnsConfig.Any(x => x.Type is "MX" or "TXT"), Is.False);
    }

    [Test]
    public void ItShouldEmitEmailRecordsWhenTenantMailIsEnabled()
    {
        var configuration = ConfigurationWithDns(
            new DnsConfigurationSet("131.164.170.62", "identity-host.example"),
            email: TenantMailEnabled());
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration(new AsciiDomainName("frodo.example.com"));

        // One MX per node, preference by list order
        var mx = dnsConfig.Where(x => x.Type == "MX").ToList();
        Assert.That(mx.Select(x => x.Value), Is.EqualTo(new[] { "10 node-a.id.pub", "20 node-b.id.pub" }));
        Assert.That(mx.TrueForAll(x => x.Name == "" && x.Optional), Is.True);

        var txt = dnsConfig.Where(x => x.Type == "TXT").ToList();
        Assert.That(txt.Single(x => x.Name == "").Value, Is.EqualTo("v=spf1 include:_spf.id.pub -all"));
        Assert.That(txt.Single(x => x.Name == "_dmarc").Value,
            Is.EqualTo("v=DMARC1; p=reject; rua=mailto:dmarc-reports@id.pub"));
        Assert.That(txt.Single(x => x.Name == "_mta-sts").Value,
            Does.StartWith("v=STSv1; id="));
        Assert.That(txt.Single(x => x.Name == "_smtp._tls").Value,
            Is.EqualTo("v=TLSRPTv1; rua=mailto:tls-reports@id.pub"));
        Assert.That(txt.TrueForAll(x => x.Optional), Is.True);

        var mtaSts = dnsConfig.Single(x => x.Type == "CNAME" && x.Name == "mta-sts");
        Assert.That(mtaSts.Value, Is.EqualTo("identity-host.example"));
        Assert.That(mtaSts.Domain, Is.EqualTo("mta-sts.frodo.example.com"));
        Assert.That(mtaSts.Optional, Is.True);

        // The pre-email records are unchanged and stay required
        Assert.That(dnsConfig.Count(x => !x.Optional), Is.EqualTo(4));
    }

    [Test]
    public void ItShouldIgnoreOptionalRecordsInTheVerdict()
    {
        // A failing optional record (mta-sts CNAME on a manual-records tenant) must not
        // fail validation - the certificate DNS gate consumes this rule
        var withFailingOptionals = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            new() { Type = "CNAME", Optional = true, Status = DnsLookupRecordStatus.DomainOrRecordNotFound },
            new() { Type = "MX", Optional = true, Status = DnsLookupRecordStatus.IncorrectValue },
            new() { Type = "TXT", Optional = true, Status = DnsLookupRecordStatus.DomainOrRecordNotFound },
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(withFailingOptionals), Is.True);
    }

    //
    // MX/TXT verification semantics (mocked lookups, through GetExternalDomainDnsStatusAsync)
    //

    private static DnsLookupService CreateDnsLookupService(OdinConfiguration configuration, ILookupClient dnsClient)
    {
        var authoritativeDnsLookup = new AuthoritativeDnsLookup(
            new Mock<ILogger<AuthoritativeDnsLookup>>().Object, dnsClient);
        var dnssecLookup = new DnssecLookup(
            new Mock<ILogger<DnssecLookup>>().Object, dnsClient, authoritativeDnsLookup);
        return new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, dnsClient, authoritativeDnsLookup,
            dnssecLookup);
    }

    private static Mock<ILookupClient> NewMockedLookupClient()
    {
        var mock = new Mock<ILookupClient>();
        // Default: every question resolves to an empty answer set
        mock.Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.IsAny<DnsQuestion>(),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockedResponse([]));
        return mock;
    }

    private static void SetupQuery(Mock<ILookupClient> mock, QueryType type, string fqdn, params DnsResourceRecord[] answers)
    {
        mock.Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.Is<DnsQuestion>(q => q.QuestionType == type && q.QueryName.Value == fqdn + "."),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockedResponse(answers));
    }

    private static IDnsQueryResponse MockedResponse(DnsResourceRecord[] answers)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers);
        mock.SetupGet(x => x.Authorities).Returns([]);
        mock.SetupGet(x => x.NameServer).Returns(new NameServer(System.Net.IPAddress.Loopback));
        return mock.Object;
    }

    private static ResourceRecordInfo Info(string owner, ResourceRecordType type) =>
        new(DnsString.Parse(owner), type, QueryClass.IN, 3600, 0);

    private static ARecord A(string owner, string ip) =>
        new(Info(owner, ResourceRecordType.A), System.Net.IPAddress.Parse(ip));

    private static AaaaRecord Aaaa(string owner, string ip) =>
        new(Info(owner, ResourceRecordType.AAAA), System.Net.IPAddress.Parse(ip));

    private static CNameRecord Cname(string owner, string target) =>
        new(Info(owner, ResourceRecordType.CNAME), DnsString.Parse(target));

    private static MxRecord Mx(string owner, ushort preference, string exchange) =>
        new(Info(owner, ResourceRecordType.MX), preference, DnsString.Parse(exchange));

    private static TxtRecord Txt(string owner, params string[] chunks) =>
        new(Info(owner, ResourceRecordType.TXT), chunks, chunks);

    private const string TestDomain = "frodo.example.com";

    private static Mock<ILookupClient> MockedHappyPathLookups()
    {
        var mock = NewMockedLookupClient();
        SetupQuery(mock, QueryType.A, TestDomain, A(TestDomain, "131.164.170.62"));
        SetupQuery(mock, QueryType.CNAME, TestDomain, Cname(TestDomain, "identity-host.example."));
        SetupQuery(mock, QueryType.CNAME, $"capi.{TestDomain}", Cname($"capi.{TestDomain}", "identity-host.example."));
        SetupQuery(mock, QueryType.CNAME, $"file.{TestDomain}", Cname($"file.{TestDomain}", "identity-host.example."));
        SetupQuery(mock, QueryType.CNAME, $"mta-sts.{TestDomain}", Cname($"mta-sts.{TestDomain}", "identity-host.example."));
        // Extra MX beside ours and a foreign TXT beside our SPF - both are normal and tolerated
        SetupQuery(mock, QueryType.MX, TestDomain,
            Mx(TestDomain, 10, "node-a.id.pub."),
            Mx(TestDomain, 20, "node-b.id.pub."),
            Mx(TestDomain, 30, "backup.somewhere-else.example."));
        SetupQuery(mock, QueryType.TXT, TestDomain,
            Txt(TestDomain, "google-site-verification=abc123"),
            Txt(TestDomain, "v=spf1 include:_spf.id.pub -all"));
        SetupQuery(mock, QueryType.TXT, $"_dmarc.{TestDomain}",
            Txt($"_dmarc.{TestDomain}", "v=DMARC1; p=reject; rua=mailto:dmarc-reports@id.pub"));
        SetupQuery(mock, QueryType.TXT, $"_smtp._tls.{TestDomain}",
            Txt($"_smtp._tls.{TestDomain}", "v=TLSRPTv1; rua=mailto:tls-reports@id.pub"));
        return mock;
    }

    private static async Task<List<DnsConfig>> RunExternalStatusAsync(Mock<ILookupClient> mock)
    {
        var configuration = ConfigurationWithDns(
            new DnsConfigurationSet("131.164.170.62", "identity-host.example"),
            email: TenantMailEnabled());

        // The _mta-sts TXT value embeds the policy id - compute the expectation the same way
        var mtaStsValue = $"v=STSv1; id={Odin.Services.Email.MtaStsPolicy.ComputeId(configuration.Email.TenantMail.MxNodes)}";
        SetupQuery(mock, QueryType.TXT, $"_mta-sts.{TestDomain}", Txt($"_mta-sts.{TestDomain}", mtaStsValue));

        var service = CreateDnsLookupService(configuration, mock.Object);
        var (_, dnsConfigs) = await service.GetExternalDomainDnsStatusAsync(new AsciiDomainName(TestDomain));
        return dnsConfigs;
    }

    [Test]
    public async Task ItShouldVerifyMxAndTxtRecordsBySetContainment()
    {
        var dnsConfigs = await RunExternalStatusAsync(MockedHappyPathLookups());

        // Multi-target MX and foreign records beside ours are tolerated: OUR values present = success
        Assert.That(dnsConfigs.Where(x => x.Type is "MX" or "TXT")
            .All(x => x.Status == DnsLookupRecordStatus.Success), Is.True);
        Assert.That(dnsConfigs.Single(x => x.Name == "mta-sts").Status, Is.EqualTo(DnsLookupRecordStatus.Success));
    }

    [Test]
    public async Task ItShouldFailMxWhenOurValueIsAbsent()
    {
        var mock = MockedHappyPathLookups();
        SetupQuery(mock, QueryType.MX, TestDomain, Mx(TestDomain, 10, "unrelated.example."));

        var dnsConfigs = await RunExternalStatusAsync(mock);

        Assert.That(dnsConfigs.Where(x => x.Type == "MX")
            .All(x => x.Status == DnsLookupRecordStatus.IncorrectValue), Is.True);
    }

    [Test]
    public async Task ItShouldReassembleChunkedTxtRecordsBeforeComparing()
    {
        var mock = MockedHappyPathLookups();
        // A >255-byte TXT value arrives as multiple character strings of one record
        SetupQuery(mock, QueryType.TXT, TestDomain,
            Txt(TestDomain, "v=spf1 include:", "_spf.id.pub -all"));

        var dnsConfigs = await RunExternalStatusAsync(mock);

        Assert.That(dnsConfigs.Single(x => x.Type == "TXT" && x.Name == "").Status,
            Is.EqualTo(DnsLookupRecordStatus.Success));
    }

    [Test]
    public async Task ItShouldNotApplyTheAaaaBailToMxAndTxtRecords()
    {
        var mock = MockedHappyPathLookups();
        SetupQuery(mock, QueryType.AAAA, TestDomain, Aaaa(TestDomain, "::1"));

        var dnsConfigs = await RunExternalStatusAsync(mock);

        // The A record trips the AAAA bail as before...
        Assert.That(dnsConfigs.Single(x => x.Type == "A").Status,
            Is.EqualTo(DnsLookupRecordStatus.AaaaRecordsNotSupported));
        // ...but apex MX/TXT legitimately coexist with AAAA and verify normally
        Assert.That(dnsConfigs.Where(x => x.Type is "MX" or "TXT")
            .All(x => x.Status == DnsLookupRecordStatus.Success), Is.True);
    }
}
