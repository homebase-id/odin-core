using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.JobManagement;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

public class IdentityRegistrationServiceTest
{
    private readonly Mock<ILogger<IdentityRegistrationService>> _loggerMock = new();
    private readonly Mock<IIdentityRegistry> _registry = new();
    private readonly Mock<IDnsRestClient> _dnsRestClient = new();
    private readonly Mock<IDynamicHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IJobManager> _jobManager = new();

    // Shorthand for the typed-domain boundary in tests
    private static AsciiDomainName D(string domain) => new(domain);

    private IdentityRegistrationService CreateIdentityRegistrationService(OdinConfiguration configuration)
    {
        var authorativeDnsLookup = new AuthoritativeDnsLookup(new Mock<ILogger<AuthoritativeDnsLookup>>().Object, new LookupClient());
        var dnssecLookup = new DnssecLookup(
            new Mock<ILogger<DnssecLookup>>().Object, new LookupClient(), authorativeDnsLookup);
        var dnsLookupService = new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, new LookupClient(), authorativeDnsLookup,
            dnssecLookup);

        return new IdentityRegistrationService(
            _loggerMock.Object,
            _registry.Object,
            configuration,
            _dnsRestClient.Object,
            _httpClientFactory.Object,
            dnsLookupService,
            _jobManager.Object);
    }

    //

    private static OdinConfiguration ConfigurationWithInvitationCodes(
        List<string> invitationCodes,
        List<string> invitationCodesWithoutPublicWebPresence)
    {
        return new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                InvitationCodes = invitationCodes,
                InvitationCodesWithoutPublicWebPresence = invitationCodesWithoutPublicWebPresence,
            }
        };
    }

    //

    [Test]
    public async Task ItShouldValidateInvitationCodesFromBothConfiguredLists()
    {
        var configuration = ConfigurationWithInvitationCodes(["with-presence"], ["no-presence"]);
        var registration = CreateIdentityRegistrationService(configuration);

        Assert.That(await registration.IsInvitationCodeNeeded(), Is.True);

        Assert.That(await registration.IsValidInvitationCode("with-presence"), Is.True);
        Assert.That(await registration.IsValidInvitationCode("WITH-PRESENCE"), Is.True);
        Assert.That(await registration.IsValidInvitationCode("no-presence"), Is.True);
        Assert.That(await registration.IsValidInvitationCode("NO-PRESENCE"), Is.True);

        Assert.That(await registration.IsValidInvitationCode("wrong"), Is.False);
        Assert.That(await registration.IsValidInvitationCode(""), Is.False);
        Assert.That(await registration.IsValidInvitationCode(null!), Is.False);

        // 'rebuild' is no longer a hardcoded bypass; it must be configured to be valid
        Assert.That(await registration.IsValidInvitationCode("rebuild"), Is.False);
    }

    //

    [Test]
    public async Task ItShouldAllowAnyCodeWhenNoCodesAreConfigured()
    {
        var configuration = ConfigurationWithInvitationCodes([], []);
        var registration = CreateIdentityRegistrationService(configuration);

        Assert.That(await registration.IsInvitationCodeNeeded(), Is.False);
        Assert.That(await registration.IsValidInvitationCode("anything"), Is.True);
        Assert.That(await registration.IsValidInvitationCode(null!), Is.True);
        Assert.That(await registration.CodeGrantsPublicWebPresence(null!), Is.True);
    }

    //

    [Test]
    public async Task ItShouldResolvePublicWebPresenceFromInvitationCode()
    {
        var configuration = ConfigurationWithInvitationCodes(["with-presence"], ["no-presence"]);
        var registration = CreateIdentityRegistrationService(configuration);

        Assert.That(await registration.CodeGrantsPublicWebPresence("with-presence"), Is.True);
        Assert.That(await registration.CodeGrantsPublicWebPresence("no-presence"), Is.False);
        Assert.That(await registration.CodeGrantsPublicWebPresence("NO-PRESENCE"), Is.False);
        Assert.That(await registration.CodeGrantsPublicWebPresence("unknown"), Is.True);
    }

    //

    [Test]
    public async Task ItShouldRegisterIdentityWithPublicWebPresenceBasedOnInvitationCode()
    {
        var configuration = ConfigurationWithInvitationCodes(["with-presence"], ["no-presence"]);
        var registration = CreateIdentityRegistrationService(configuration);

        IdentityRegistrationRequest? capturedRequest = null;
        _registry.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((IdentityRegistration?)null!);
        _registry.Setup(r => r.AddRegistration(It.IsAny<IdentityRegistrationRequest>()))
            .Callback<IdentityRegistrationRequest>(r => capturedRequest = r)
            .ReturnsAsync(System.Guid.NewGuid());

        await registration.CreateIdentityOnDomainAsync(D("frodo.example.com"), "frodo@example.com", "free", "no-presence");
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.EnablePublicWebPresence, Is.False);

        await registration.CreateIdentityOnDomainAsync(D("sam.example.com"), "sam@example.com", "free", "with-presence");
        Assert.That(capturedRequest!.EnablePublicWebPresence, Is.True);
    }

    //

    public enum Resolver
    {
        Authoritative,
        External
    };

    //

    [Test, Explicit]
    [TestCase("yagni.dk", Resolver.Authoritative, "135.181.203.146", "identity-host-1.ravenhosting.cloud", true, DnsLookupRecordStatus.Success, DnsLookupRecordStatus.DomainOrRecordNotFound, DnsLookupRecordStatus.Success)]
    [TestCase("yagni.dk", Resolver.External, "135.181.203.146", "identity-host-1.ravenhosting.cloud", true, DnsLookupRecordStatus.Success, DnsLookupRecordStatus.DomainOrRecordNotFound, DnsLookupRecordStatus.Success)]
    public async Task ItShouldGetAuthoritativeDnsStatus(
        string domain,
        Resolver resolver,
        string apexARecord,
        string apexAliasRecord,
        bool success,
        DnsLookupRecordStatus apexARecordStatus,
        DnsLookupRecordStatus apexAliasRecordStatus,
        DnsLookupRecordStatus cnameRecordStatus)
    {
        var (resolved, dnsConfigs) = await GetDnsStatus(resolver, domain, apexARecord, apexAliasRecord);
        Assert.That(resolved, Is.EqualTo(success));

        {
            var record = dnsConfigs.First(x => x is { Name: "", Type: "A" });
            Assert.That(record.Status, Is.EqualTo(apexARecordStatus));
        }
        {
            var record = dnsConfigs.First(x => x is { Name: "", Type: "ALIAS" });
            Assert.That(record.Status, Is.EqualTo(apexAliasRecordStatus));
        }
        {
            var record = dnsConfigs.First(x => x is { Name: DnsConfigurationSet.PrefixCertApi });
            Assert.That(record.Status, Is.EqualTo(cnameRecordStatus));
        }
        {
            var record = dnsConfigs.First(x => x is { Name: DnsConfigurationSet.PrefixFile });
            Assert.That(record.Status, Is.EqualTo(cnameRecordStatus));
        }
    }

    //

    private async Task<(bool, List<DnsConfig>)> GetDnsStatus(Resolver resolver, string domain, string apexARecord, string apexAliasRecord)
    {
        var configuration = new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = new DnsConfigurationSet(apexARecord, apexAliasRecord),
                ManagedDomainApexes = new List<OdinConfiguration.RegistrySection.ManagedDomainApex>
                {
                    new()
                    {
                        Apex = "demo.rocks",
                        PrefixLabels = new List<string>
                        {
                            "First name", "Last name"
                        }
                    }
                },
                DnsResolvers = new List<string> {"1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222"}
            }
        };

        var registration = CreateIdentityRegistrationService(configuration);

        if (resolver == Resolver.Authoritative)
        {
            return await registration.GetAuthoritativeDomainDnsStatus(D(domain), CancellationToken.None);
        }

        return await registration.GetExternalDomainDnsStatus(D(domain), CancellationToken.None);
    }

    //
    // Own-domain zone lifecycle
    //

    private static OdinConfiguration ConfigurationWithZoneHosting(bool configured = true)
    {
        return new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                PowerDnsApiKey = configured ? "top-secret" : "",
                DnsConfigurationSet = new DnsConfigurationSet(
                    "131.164.170.62",
                    "identity-host.example",
                    configured ? ["ns1.example", "ns2.example"] : [],
                    "admin@example.com"),
                ManagedDomainApexes =
                [
                    new() { Apex = "demo.rocks", PrefixLabels = ["First name", "Last name"] }
                ],
            }
        };
    }

    [Test]
    public async Task ItShouldSkipZoneCreationWhenZoneHostingIsNotConfigured()
    {
        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting(configured: false));

        Assert.That(registration.CanHostOwnDomainZones, Is.False);
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.NotConfigured));
        _dnsRestClient.VerifyNoOtherCalls();
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldCreateZoneWithNameserversAndRecordsForOwnDomain()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(false);
        // Domain-control proof: an identity is registered for the domain
        _registry.Setup(r => r.GetAsync("frodo.example.com")).ReturnsAsync(new IdentityRegistration());

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        Assert.That(registration.CanHostOwnDomainZones, Is.True);
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));
        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.Created));

        _dnsRestClient.Verify(c => c.CreateZone(
            "frodo.example.com.",
            It.Is<string[]>(ns => ns.SequenceEqual(new[] { "ns1.example.", "ns2.example." })),
            "admin@example.com"), Times.Once);
        // Apex A record (empty name = apex), capi + file CNAMEs; no ALIAS, no NS rrset calls
        _dnsRestClient.Verify(c => c.CreateARecords(
            "frodo.example.com.", "", It.Is<IEnumerable<string>>(v => v.Single() == "131.164.170.62")), Times.Once);
        _dnsRestClient.Verify(c => c.CreateCnameRecords(
            "frodo.example.com.", "capi", "identity-host.example."), Times.Once);
        _dnsRestClient.Verify(c => c.CreateCnameRecords(
            "frodo.example.com.", "file", "identity-host.example."), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldNotRecreateAnExistingZone()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);
        _registry.Setup(r => r.GetAsync("frodo.example.com")).ReturnsAsync(new IdentityRegistration());

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));
        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.Created));

        _dnsRestClient.Verify(c => c.CreateZone(
            It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        // Records are still (re)placed so re-running converges
        _dnsRestClient.Verify(c => c.CreateARecords(
            "frodo.example.com.", "", It.IsAny<IEnumerable<string>>()), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public void ItShouldRefuseZoneCreationForManagedDomains()
    {
        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        Assert.ThrowsAsync<Odin.Core.Exceptions.OdinSystemException>(
            () => registration.CreateOwnDomainZone(D("frodo.baggins.demo.rocks")));
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldNotOfferManagedDomainsAsOwnDomains()
    {
        // The UI's availability check and the create-own-domain-zone endpoint's guard both
        // run through IsOwnDomainAvailable - managed apexes and their subdomains must be
        // rejected there, before any zone logic is reached
        _registry.Setup(r => r.CanAddNewRegistration(It.IsAny<string>())).ReturnsAsync(true);
        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        Assert.That(await registration.IsOwnDomainAvailable(D("demo.rocks")), Is.False);
        Assert.That(await registration.IsOwnDomainAvailable(D("frodo.baggins.demo.rocks")), Is.False);
        Assert.That(await registration.IsOwnDomainAvailable(D("FRODO.BAGGINS.DEMO.ROCKS")), Is.False);
        Assert.That(await registration.IsOwnDomainAvailable(D("frodo.example.com")), Is.True);
    }

    [Test]
    public async Task ItShouldRefuseAZoneThatAlreadyExistsWithForeignRecords()
    {
        // Shared-PowerDNS scenario: michael.seifert.page is delegated to ns1/ns2 and its
        // zone serves a LIVE identity in another environment (different apex A). This
        // environment has no registration for it, and both environments answer to the same
        // nameserver names, so the delegation proof is ambiguous ("proven" here via a
        // mocked lookup service). The zone must be refused untouched - repopulating it
        // would hijack the other environment's identity.
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("michael.seifert.page.")).ReturnsAsync(true);
        _dnsRestClient.Setup(c => c.GetZone("michael.seifert.page.")).ReturnsAsync(
            new Odin.Services.Dns.PowerDns.ZoneWithRecords
            {
                rrsets =
                [
                    new()
                    {
                        name = "michael.seifert.page.", type = "A",
                        records = [new() { content = "203.0.113.99" }] // the OTHER environment's ingress
                    }
                ]
            });
        // The other environment's registration is invisible to this environment's registry
        _registry.Setup(r => r.GetAsync("michael.seifert.page")).ReturnsAsync((IdentityRegistration?)null!);

        var dnsLookupService = new Mock<IDnsLookupService>();
        dnsLookupService
            .Setup(s => s.IsDomainDelegatedToUsAsync(D("michael.seifert.page"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // shared nameserver identity: delegation looks like ours

        var registration = new IdentityRegistrationService(
            _loggerMock.Object,
            _registry.Object,
            ConfigurationWithZoneHosting(),
            _dnsRestClient.Object,
            _httpClientFactory.Object,
            dnsLookupService.Object,
            _jobManager.Object);

        var result = await registration.CreateOwnDomainZone(D("michael.seifert.page"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.ZoneAlreadyHosted));
        _dnsRestClient.Verify(c => c.CreateZone(
            It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        _dnsRestClient.Verify(c => c.CreateARecords(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _dnsRestClient.Verify(c => c.CreateCnameRecords(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldTreatAnExistingZoneWithOurRecordsAsOurs()
    {
        // Normal same-environment flow: Validate created the zone earlier (apex A already
        // ours), identity not yet registered - a second Validate must succeed, not refuse
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);
        _dnsRestClient.Setup(c => c.GetZone("frodo.example.com.")).ReturnsAsync(
            new Odin.Services.Dns.PowerDns.ZoneWithRecords
            {
                rrsets =
                [
                    new()
                    {
                        name = "frodo.example.com.", type = "A",
                        records = [new() { content = "131.164.170.62" }] // OUR apex A
                    }
                ]
            });
        // Registered locally so the control gate passes without live DNS; the point under
        // test is that the zone-content branch is not even consulted for owners, and that
        // an existing zone with our records proceeds to populate
        _registry.Setup(r => r.GetAsync("frodo.example.com")).ReturnsAsync(new IdentityRegistration());

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.Created));
        _dnsRestClient.Verify(c => c.CreateZone(
            It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        _dnsRestClient.Verify(c => c.CreateARecords(
            "frodo.example.com.", "", It.IsAny<IEnumerable<string>>()), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldRefuseZoneCreationInsideAHostedZone()
    {
        // demo.id.pub must never become its own zone while we host id.pub: the child
        // zone would shadow that part of the parent. Registration state is irrelevant.
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("id.pub.")).ReturnsAsync(true);
        _registry.Setup(r => r.GetAsync("demo.id.pub")).ReturnsAsync(new IdentityRegistration());

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var result = await registration.CreateOwnDomainZone(D("demo.id.pub"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.ShadowsHostedZone));
        _dnsRestClient.Verify(c => c.CreateZone(
            It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        _dnsRestClient.Verify(c => c.CreateARecords(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _dnsRestClient.Invocations.Clear();
    }

    [Test, Explicit] // performs live DNS lookups (delegation + record checks)
    public async Task ItShouldRefuseZoneCreationWhenDomainControlIsNotProven()
    {
        _dnsRestClient.Invocations.Clear();
        _registry.Setup(r => r.GetAsync("frodo.example.com")).ReturnsAsync((IdentityRegistration?)null!);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.ControlNotProven),
            "no registration, no delegation to our nameservers, no valid records -> refused");
        _dnsRestClient.Verify(c => c.CreateZone(
            It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldDeleteDnsRecordsOrZoneDependingOnDomainKind()
    {
        _dnsRestClient.Invocations.Clear();

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        // Managed domain: records removed from the shared apex zone, zone untouched
        await registration.DeleteDnsRecordsForDomain(D("frodo.baggins.demo.rocks"));
        _dnsRestClient.Verify(c => c.DeleteARecords("demo.rocks.", "frodo.baggins"), Times.Once);
        _dnsRestClient.Verify(c => c.DeleteCnameRecords("demo.rocks.", "capi.frodo.baggins"), Times.Once);
        _dnsRestClient.Verify(c => c.DeleteCnameRecords("demo.rocks.", "file.frodo.baggins"), Times.Once);
        _dnsRestClient.Verify(c => c.DeleteZone(It.IsAny<string>()), Times.Never);

        // Own domain: zone deleted
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);
        await registration.DeleteDnsRecordsForDomain(D("frodo.example.com"));
        _dnsRestClient.Verify(c => c.DeleteZone("frodo.example.com."), Times.Once);

        // DNS API failure never propagates
        _dnsRestClient.Setup(c => c.DeleteARecords(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.Exception("boom"));
        Assert.DoesNotThrowAsync(() => registration.DeleteDnsRecordsForDomain(D("sam.gamgee.demo.rocks")));
        _dnsRestClient.Invocations.Clear();
    }

    // --- On-activation records (per-tenant values, e.g. DKIM TXT - docs/email-dns-plan.md) ---

    private static List<DnsConfig> TwoOnActivationTxtRecords(string domainName) =>
    [
        new()
        {
            Type = "TXT", Name = "s1._domainkey", Domain = $"s1._domainkey.{domainName}",
            Value = "v=DKIM1; k=ed25519; p=AAAA", AltValue = "v=DKIM1; k=ed25519; p=AAAA",
            Description = "DKIM key (ed25519)", Optional = true,
        },
        new()
        {
            Type = "TXT", Name = "s2._domainkey", Domain = $"s2._domainkey.{domainName}",
            Value = "v=DKIM1; k=rsa; p=BBBB", AltValue = "v=DKIM1; k=rsa; p=BBBB",
            Description = "DKIM key (rsa)", Optional = true,
        },
    ];

    [Test]
    public async Task ItShouldWriteOnActivationRecordsAsPrefixedEntriesForManagedDomains()
    {
        _dnsRestClient.Invocations.Clear();

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var written = await registration.WriteOnActivationRecords(
            D("frodo.baggins.demo.rocks"), TwoOnActivationTxtRecords("frodo.baggins.demo.rocks"));

        Assert.That(written, Is.True);
        _dnsRestClient.Verify(c => c.CreateTxtRecords(
            "demo.rocks.", "s1._domainkey.frodo.baggins",
            It.Is<IEnumerable<string>>(v => v.Single() == "v=DKIM1; k=ed25519; p=AAAA")), Times.Once);
        _dnsRestClient.Verify(c => c.CreateTxtRecords(
            "demo.rocks.", "s2._domainkey.frodo.baggins",
            It.Is<IEnumerable<string>>(v => v.Single() == "v=DKIM1; k=rsa; p=BBBB")), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldWriteOnActivationRecordsIntoTheOwnDomainZone()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var written = await registration.WriteOnActivationRecords(
            D("frodo.example.com"), TwoOnActivationTxtRecords("frodo.example.com"));

        Assert.That(written, Is.True);
        _dnsRestClient.Verify(c => c.CreateTxtRecords(
            "frodo.example.com.", "s1._domainkey", It.IsAny<IEnumerable<string>>()), Times.Once);
        _dnsRestClient.Verify(c => c.CreateTxtRecords(
            "frodo.example.com.", "s2._domainkey", It.IsAny<IEnumerable<string>>()), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldReportManualDnsTenantsAsNotWritable()
    {
        _dnsRestClient.Invocations.Clear();
        // Not under a managed apex, and no hosted zone for the domain
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(false);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var written = await registration.WriteOnActivationRecords(
            D("frodo.example.com"), TwoOnActivationTxtRecords("frodo.example.com"));

        Assert.That(written, Is.False);
        _dnsRestClient.Verify(c => c.CreateTxtRecords(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldReportManagedDomainsAsNotWritableWithoutPowerDnsAccess()
    {
        // An identity host without PowerDNS access cannot write the shared apex zone;
        // the caller must fall back to showing the records as instructions
        _dnsRestClient.Invocations.Clear();

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting(configured: false));
        var written = await registration.WriteOnActivationRecords(
            D("frodo.baggins.demo.rocks"), TwoOnActivationTxtRecords("frodo.baggins.demo.rocks"));

        Assert.That(written, Is.False);
        _dnsRestClient.VerifyNoOtherCalls();
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldDeleteOnActivationRecordsForManagedDomains()
    {
        _dnsRestClient.Invocations.Clear();

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var deleted = await registration.DeleteOnActivationRecords(
            D("frodo.baggins.demo.rocks"), TwoOnActivationTxtRecords("frodo.baggins.demo.rocks"));

        Assert.That(deleted, Is.True);
        _dnsRestClient.Verify(c => c.DeleteTxtRecords("demo.rocks.", "s1._domainkey.frodo.baggins"), Times.Once);
        _dnsRestClient.Verify(c => c.DeleteTxtRecords("demo.rocks.", "s2._domainkey.frodo.baggins"), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldDeleteAnExistingOwnDomainZoneAndNeverThrow()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        await registration.DeleteOwnDomainZone(D("frodo.example.com"));
        _dnsRestClient.Verify(c => c.DeleteZone("frodo.example.com."), Times.Once);

        // Managed domain -> no zone deletion
        await registration.DeleteOwnDomainZone(D("frodo.baggins.demo.rocks"));
        _dnsRestClient.Verify(c => c.DeleteZone("frodo.baggins.demo.rocks."), Times.Never);

        // DNS API blowing up must not propagate
        _dnsRestClient.Setup(c => c.ZoneExists("sam.example.com.")).ThrowsAsync(new System.Exception("boom"));
        Assert.DoesNotThrowAsync(() => registration.DeleteOwnDomainZone(D("sam.example.com")));
        _dnsRestClient.Invocations.Clear();
    }

    //
    // DNSSEC status (docs/byod-dnssec-plan.md)
    //

    private static DsRecordData Ds(int keyTag, string digest = "aabbcc")
    {
        return new DsRecordData(keyTag, 13, 2, digest);
    }

    [Test]
    public async Task ItShouldReportDnssecNotConfiguredWithoutZoneHosting()
    {
        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting(configured: false));

        var result = await registration.GetDnssecStatusAsync(D("frodo.example.com"));

        Assert.That(result.Status, Is.EqualTo(DnssecStatus.NotConfigured));
        _dnsRestClient.VerifyNoOtherCalls();
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldReportDnssecZoneNotHostedForMissingZonesAndManagedDomains()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(false);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        var noZone = await registration.GetDnssecStatusAsync(D("frodo.example.com"));
        Assert.That(noZone.Status, Is.EqualTo(DnssecStatus.ZoneNotHosted));

        // Managed domains never have their own zone; PowerDNS is not even consulted
        var managed = await registration.GetDnssecStatusAsync(D("frodo.baggins.demo.rocks"));
        Assert.That(managed.Status, Is.EqualTo(DnssecStatus.ZoneNotHosted));
        _dnsRestClient.Verify(c => c.ZoneExists("frodo.baggins.demo.rocks."), Times.Never);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldComposeTheDnssecVerdictFromZoneAndParentState()
    {
        // Zone hosted and signed (PowerDNS side), parent signed, no DS yet (generic
        // DNS side, mocked at the IDnsLookupService seam) -> DsMissing with the DS
        // values the user must publish
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);
        _dnsRestClient.Setup(c => c.GetZoneDsRecords("frodo.example.com.")).ReturnsAsync([Ds(46082)]);

        var dnsLookupService = new Mock<IDnsLookupService>();
        dnsLookupService
            .Setup(x => x.IsParentZoneSignedAsync(D("frodo.example.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        dnsLookupService
            .Setup(x => x.GetParentDsRecordsAsync(D("frodo.example.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var registration = new IdentityRegistrationService(
            _loggerMock.Object,
            _registry.Object,
            ConfigurationWithZoneHosting(),
            _dnsRestClient.Object,
            _httpClientFactory.Object,
            dnsLookupService.Object,
            _jobManager.Object);

        var result = await registration.GetDnssecStatusAsync(D("frodo.example.com"));

        Assert.That(result.Status, Is.EqualTo(DnssecStatus.DsMissing));
        Assert.That(result.ParentZoneSigned, Is.True);
        Assert.That(result.OurDsRecords.Single().KeyTag, Is.EqualTo(46082));
        Assert.That(result.ParentDsRecords, Is.Empty);
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldPublishCdsMetadataWhenEnsuringAZone()
    {
        // CDS/CDNSKEY publication rides every successful zone ensure (idempotent),
        // so the CLI backfill upgrades existing zones too
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(false);
        _registry.Setup(r => r.GetAsync("frodo.example.com")).ReturnsAsync(new IdentityRegistration());

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        var result = await registration.CreateOwnDomainZone(D("frodo.example.com"));

        Assert.That(result, Is.EqualTo(CreateOwnDomainZoneResult.Created));
        _dnsRestClient.Verify(c => c.PublishCdsRecords("frodo.example.com."), Times.Once);
        _dnsRestClient.Invocations.Clear();
    }
}
