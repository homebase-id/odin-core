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

}