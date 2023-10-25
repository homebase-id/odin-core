using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Dns;
using Odin.Core.Services.Email;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Registry.Registration;

namespace Odin.Core.Services.Tests.Registry.Registration;

public class IdentityRegistrationServiceTest
{
    private readonly Mock<ILogger<IdentityRegistrationService>> _loggerMock = new();
    private readonly Mock<IIdentityRegistry> _registry = new();
    private readonly Mock<IDnsRestClient> _dnsRestClient = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IEmailSender> _emailSender = new();

    private IdentityRegistrationService CreateIdentityRegistrationService(OdinConfiguration configuration)
    {
        var authorativeDnsLookup = new AuthorativeDnsLookup(new Mock<ILogger<AuthorativeDnsLookup>>().Object);
        var dnsLookupService = new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, authorativeDnsLookup);

        return new IdentityRegistrationService(
            _loggerMock.Object,
            _registry.Object,
            configuration,
            _dnsRestClient.Object,
            _httpClientFactory.Object,
            _emailSender.Object,
            dnsLookupService);
    }

    //

    public enum Resolver
    {
        Authorative,
        External
    };

    //

    [Test, Explicit]
    [TestCase("example.com", Resolver.Authorative, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.IncorrectValue, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("www.example.com", Resolver.Authorative, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.IncorrectValue, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("foo.bar.www.example.com", Resolver.Authorative, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.DomainOrRecordNotFound, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("yagni.dk", Resolver.Authorative, "135.181.203.146", "identity-host-1.ravenhosting.cloud", true, DnsLookupRecordStatus.Success, DnsLookupRecordStatus.Success)]
    [TestCase("example.com", Resolver.External, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.IncorrectValue, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("www.example.com", Resolver.External, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.IncorrectValue, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("foo.bar.www.example.com", Resolver.External, "131.164.170.62", "identity-host.sebbarg.net", false, DnsLookupRecordStatus.DomainOrRecordNotFound, DnsLookupRecordStatus.DomainOrRecordNotFound)]
    [TestCase("yagni.dk", Resolver.External, "135.181.203.146", "identity-host-1.ravenhosting.cloud", true, DnsLookupRecordStatus.Success, DnsLookupRecordStatus.Success)]
    public async Task ItShouldGetAuthorativeDnsStatus(
        string domain,
        Resolver resolver,
        string apexARecord,
        string apexAliasRecord,
        bool success,
        DnsLookupRecordStatus apexRecordStatus,
        DnsLookupRecordStatus cnameRecordStatus)
    {
        var (resolved, dnsConfigs) = await GetDnsStatus(resolver, domain, apexARecord, apexAliasRecord);
        Assert.That(resolved, Is.EqualTo(success));

        {
            var record = dnsConfigs.First(x => x is { Name: "", Type: "A" });
            Assert.That(record.Status, Is.EqualTo(apexRecordStatus));
        }
        {
            var record = dnsConfigs.First(x => x is { Name: "", Type: "ALIAS" });
            Assert.That(record.Status, Is.EqualTo(apexRecordStatus));
        }
        {
            var record = dnsConfigs.First(x => x is { Name: DnsConfigurationSet.PrefixWww });
            Assert.That(record.Status, Is.EqualTo(cnameRecordStatus));
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
                DnsConfigurationSet = new DnsConfigurationSet(apexARecord, apexAliasRecord, "", "", ""),
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

        if (resolver == Resolver.Authorative)
        {
            return await registration.GetAuthorativeDomainDnsStatus(domain);
        }

        return await registration.GetExternalDomainDnsStatus(domain);
    }

}