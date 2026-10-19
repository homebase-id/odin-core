using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class HostingPlansTests
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

    private static HostingPlans Plans(Dictionary<string, string?> plans) =>
        new(new OdinConfiguration { Registry = Registry(plans) });

    private static readonly Dictionary<string, string?> FreeAndPro = new()
    {
        ["0:Id"] = "free", ["0:QuotaBytes"] = "1000",
        ["1:Id"] = "pro-100gb", ["1:QuotaBytes"] = "100000",
        ["2:Id"] = "unlimited",
    };

    [Test]
    public void NoPlansMeansNoPlans()
    {
        var quotas = Plans([]);

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
        Assert.That(Plans(FreeAndPro).QuotaFor(planId), Is.EqualTo(quota));
    }

    [TestCase("pro-100gb", true)]
    [TestCase(" UNLIMITED ", true)]
    [TestCase("no-such-plan", false)] // a typo must not quietly mean the free plan's quota
    public void OnlyAConfiguredPlanIsAssignable(string planId, bool assignable)
    {
        Assert.That(Plans(FreeAndPro).IsAssignable(planId), Is.EqualTo(assignable));
    }

    [Test]
    public void AnyPlanIsAssignableWhileQuotasAreOff()
    {
        Assert.That(Plans([]).IsAssignable("pro-100gb"), Is.True);
    }

    private static readonly Dictionary<string, string?> WithPublicWebPresence = new()
    {
        ["0:Id"] = "free",
        ["1:Id"] = "public", ["1:PublicWebPresence"] = "true",
        ["2:Id"] = "private", ["2:PublicWebPresence"] = "false",
    };

    [TestCase("public", false, true, Description = "the plan's yes wins over the identity's no")]
    [TestCase("PRIVATE", true, false, Description = "the plan's no wins over the identity's yes")]
    [TestCase("free", true, true, Description = "a plan that does not say leaves it to the identity")]
    [TestCase("free", false, false)]
    [TestCase("no-such-plan", false, false, Description = "an unknown plan gets free's, which does not say")]
    [TestCase(null, true, true)]
    public void APlanDecidesPublicWebPresenceOnlyWhenItSays(string? planId, bool identitySetting, bool expected)
    {
        Assert.That(Plans(WithPublicWebPresence).PublicWebPresenceFor(planId, identitySetting), Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void WithPlansOffTheIdentityDecidesPublicWebPresence(bool identitySetting)
    {
        Assert.That(Plans([]).PublicWebPresenceFor("public", identitySetting), Is.EqualTo(identitySetting));
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
