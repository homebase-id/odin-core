using System.Collections.Generic;
using System.Linq;
using DnsClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Util;
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

    private static OdinConfiguration ConfigurationWithDns(
        DnsConfigurationSet dnsConfigurationSet,
        string powerDnsApiKey = "top-secret")
    {
        return new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = dnsConfigurationSet,
                PowerDnsApiKey = powerDnsApiKey,
                ManagedDomainApexes = [new() { Apex = "demo.rocks", PrefixLabels = ["First", "Surname"] }],
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
}
