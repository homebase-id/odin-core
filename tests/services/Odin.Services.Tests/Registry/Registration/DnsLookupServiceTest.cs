using System.Collections.Generic;
using System.Linq;
using DnsClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

public class DnsLookupServiceTest
{
    private static DnsLookupService CreateDnsLookupService(OdinConfiguration configuration)
    {
        var authoritativeDnsLookup = new AuthoritativeDnsLookup(
            new Mock<ILogger<AuthoritativeDnsLookup>>().Object, new LookupClient());
        return new DnsLookupService(
            new Mock<ILogger<DnsLookupService>>().Object, configuration, new LookupClient(), authoritativeDnsLookup);
    }

    private static OdinConfiguration ConfigurationWithDns(DnsConfigurationSet dnsConfigurationSet)
    {
        return new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = dnsConfigurationSet,
            }
        };
    }

    //

    [Test]
    public void ItShouldNotIncludeNsRecordsWhenNoNameServersAreConfigured()
    {
        var configuration = ConfigurationWithDns(new DnsConfigurationSet("131.164.170.62", "identity-host.example"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration("frodo.example.com");

        Assert.That(dnsConfig.Any(x => x.Type == "NS"), Is.False);
        Assert.That(dnsConfig.Count, Is.EqualTo(4)); // A, ALIAS, capi CNAME, file CNAME - unchanged
    }

    //

    [Test]
    public void ItShouldIncludeOneNsRecordPerConfiguredNameServer()
    {
        var configuration = ConfigurationWithDns(new DnsConfigurationSet(
            "131.164.170.62", "identity-host.example",
            ["NS1.Example.", "ns2.example"], "admin@example.com"));
        var service = CreateDnsLookupService(configuration);

        var dnsConfig = service.GetDnsConfiguration("frodo.example.com");

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
    public void ItShouldSucceedWhenAllNsRecordsAreSuccessfulRegardlessOfOtherRecords()
    {
        // Delegated mode: our nameservers serve everything; the individual record
        // checks may point wherever they like (e.g. still unresolved ALIAS)
        var dnsConfigs = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("ALIAS", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("CNAME", DnsLookupRecordStatus.DomainOrRecordNotFound),
            Record("NS", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.Success),
        };

        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(dnsConfigs), Is.True);
    }

    [Test]
    public void ItShouldFallBackToManualRuleWhenNsRecordsAreNotAllSuccessful()
    {
        // One NS missing -> not delegated; manual records decide
        var delegatedButIncomplete = new List<DnsConfig>
        {
            Record("A", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("CNAME", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.Success),
            Record("NS", DnsLookupRecordStatus.DomainOrRecordNotFound),
        };
        Assert.That(DnsLookupService.AreDnsLookupsSuccessful(delegatedButIncomplete), Is.True,
            "manual records are all good, so overall success despite incomplete delegation");

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
}
