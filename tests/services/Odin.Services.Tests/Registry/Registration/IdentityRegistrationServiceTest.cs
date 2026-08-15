using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
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

    private IdentityRegistrationService CreateIdentityRegistrationService(OdinConfiguration configuration)
    {
        var authorativeDnsLookup = new AuthoritativeDnsLookup(new Mock<ILogger<AuthoritativeDnsLookup>>().Object, new LookupClient());
        var dnsLookupService = new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, new LookupClient(), authorativeDnsLookup);

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

        await registration.CreateIdentityOnDomainAsync("frodo.example.com", "frodo@example.com", "free", "no-presence");
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.EnablePublicWebPresence, Is.False);

        await registration.CreateIdentityOnDomainAsync("sam.example.com", "sam@example.com", "free", "with-presence");
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
            return await registration.GetAuthoritativeDomainDnsStatus(domain, CancellationToken.None);
        }

        return await registration.GetExternalDomainDnsStatus(domain, CancellationToken.None);
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
        await registration.CreateOwnDomainZone("frodo.example.com");

        _dnsRestClient.VerifyNoOtherCalls();
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldCreateZoneWithNameserversAndRecordsForOwnDomain()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(false);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());

        Assert.That(registration.CanHostOwnDomainZones, Is.True);
        await registration.CreateOwnDomainZone("frodo.example.com");

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

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        await registration.CreateOwnDomainZone("frodo.example.com");

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
            () => registration.CreateOwnDomainZone("frodo.baggins.demo.rocks"));
        _dnsRestClient.Invocations.Clear();
    }

    [Test]
    public async Task ItShouldDeleteAnExistingOwnDomainZoneAndNeverThrow()
    {
        _dnsRestClient.Invocations.Clear();
        _dnsRestClient.Setup(c => c.ZoneExists("frodo.example.com.")).ReturnsAsync(true);

        var registration = CreateIdentityRegistrationService(ConfigurationWithZoneHosting());
        await registration.DeleteOwnDomainZone("frodo.example.com");
        _dnsRestClient.Verify(c => c.DeleteZone("frodo.example.com."), Times.Once);

        // Managed domain -> no zone deletion
        await registration.DeleteOwnDomainZone("frodo.baggins.demo.rocks");
        _dnsRestClient.Verify(c => c.DeleteZone("frodo.baggins.demo.rocks."), Times.Never);

        // DNS API blowing up must not propagate
        _dnsRestClient.Setup(c => c.ZoneExists("sam.example.com.")).ThrowsAsync(new System.Exception("boom"));
        Assert.DoesNotThrowAsync(() => registration.DeleteOwnDomainZone("sam.example.com"));
        _dnsRestClient.Invocations.Clear();
    }
}