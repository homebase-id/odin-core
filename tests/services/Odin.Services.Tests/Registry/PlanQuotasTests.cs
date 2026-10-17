using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class PlanQuotasTests
{
    // The keys RegistrySection requires, plus the plans under test
    private static OdinConfiguration.RegistrySection Registry(Dictionary<string, string?> plans)
    {
        var pairs = new Dictionary<string, string?>
        {
            ["Registry:ProvisioningDomain"] = "provisioning.example.com",
            ["Registry:DnsRecordValues:ApexARecords:0"] = "127.0.0.1",
            ["Registry:DnsRecordValues:ApexAliasRecord"] = "alias.example.com",
        };
        foreach (var (key, value) in plans)
        {
            pairs[$"Registry:Plans:{key}"] = value;
        }

        return new OdinConfiguration.RegistrySection(new ConfigurationBuilder().AddInMemoryCollection(pairs).Build());
    }

    private static PlanQuotas Quotas(Dictionary<string, string?> plans) =>
        new(new OdinConfiguration { Registry = Registry(plans) });

    private static readonly Dictionary<string, string?> FreeAndPro = new()
    {
        ["0:Id"] = "free", ["0:QuotaBytes"] = "1000",
        ["1:Id"] = "pro-100gb", ["1:QuotaBytes"] = "100000",
        ["2:Id"] = "unlimited",
    };

    [Test]
    public void NoPlansMeansNoQuotas()
    {
        var quotas = Quotas([]);

        Assert.That(quotas.IsEnabled, Is.False);
        Assert.That(quotas.QuotaFor("free"), Is.Null);
        Assert.That(quotas.QuotaFor(null), Is.Null);
    }

    [TestCase("free", 1000L)]
    [TestCase("PRO-100GB", 100000L)] // ids are case-insensitive
    [TestCase(" pro-100gb ", 100000L)]
    [TestCase("unlimited", null)]
    [TestCase("no-such-plan", 1000L)] // unknown plans get the free plan's quota
    [TestCase(null, 1000L)]
    public void APlanResolvesToItsQuota(string? planId, long? quota)
    {
        Assert.That(Quotas(FreeAndPro).QuotaFor(planId), Is.EqualTo(quota));
    }

    [Test]
    public void TheFreePlanIsRequired()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "pro", ["0:QuotaBytes"] = "10" }));
    }

    [Test]
    public void PlanIdsMustBeUniqueAndPresent()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["1:Id"] = "FREE" }));
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["1:QuotaBytes"] = "5" }));
    }

    [Test]
    public void QuotasCannotBeNegative()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["0:QuotaBytes"] = "-1" }));
    }
}
