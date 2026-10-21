using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class HostingPlansTests
{
    // The keys RegistrySection requires, plus the plans under test
    private static OdinConfiguration.RegistrySection Registry(Dictionary<string, string?> plans, string? upgradeUrl = null)
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
        if (upgradeUrl != null)
        {
            pairs["Registry:UpgradeUrl"] = upgradeUrl;
        }

        return new OdinConfiguration.RegistrySection(new ConfigurationBuilder().AddInMemoryCollection(pairs).Build());
    }

    private static HostingPlans Plans(Dictionary<string, string?> plans) =>
        new(new OdinConfiguration { Registry = Registry(plans) });

    // The default is not called free: free is what the registry stored for no plan before #1974
    private static readonly Dictionary<string, string?> BasicProAndUnlimited = new()
    {
        ["0:Id"] = "basic", ["0:QuotaBytes"] = "1000", ["0:Default"] = "true",
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

    [TestCase("basic", 1000L)]
    [TestCase("PRO-100GB", 100000L)] // ids are case-insensitive
    [TestCase(" pro-100gb ", 100000L)]
    [TestCase("unlimited", null)]
    [TestCase("no-such-plan", 1000L)] // unknown plans get the default plan's quota
    [TestCase("free", 1000L)] // as stored for no plan before #1974, and not in the list
    [TestCase(null, 1000L)]
    public void APlanResolvesToItsQuota(string? planId, long? quota)
    {
        Assert.That(Plans(BasicProAndUnlimited).QuotaFor(planId), Is.EqualTo(quota));
    }

    [TestCase("pro-100gb", true)]
    [TestCase("UNLIMITED", true)] // ids are case-insensitive; the caller trims
    [TestCase("no-such-plan", false)] // a typo must not quietly mean the free plan's quota
    public void OnlyAConfiguredPlanIsAssignable(string planId, bool assignable)
    {
        Assert.That(Plans(BasicProAndUnlimited).IsAssignable(planId), Is.EqualTo(assignable));
    }

    [Test]
    public void AnyPlanIsAssignableWhileQuotasAreOff()
    {
        Assert.That(Plans([]).IsAssignable("pro-100gb"), Is.True);
    }

    private static readonly Dictionary<string, string?> WithPublicWebPresence = new()
    {
        ["0:Id"] = "legacy", ["0:Default"] = "true",
        ["1:Id"] = "public", ["1:PublicWebPresence"] = "true",
        ["2:Id"] = "private", ["2:PublicWebPresence"] = "false",
    };

    [TestCase("public", false, true, Description = "the plan's yes wins over the identity's no")]
    [TestCase("PRIVATE", true, false, Description = "the plan's no wins over the identity's yes")]
    [TestCase("legacy", true, true, Description = "a plan that does not say leaves it to the identity")]
    [TestCase("legacy", false, false)]
    [TestCase("no-such-plan", false, false, Description = "an unknown plan gets the default's, which does not say")]
    [TestCase("free", true, true, Description = "stored for no plan before #1974: the default's")]
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
    public void ExactlyOneDefaultPlanIsRequired()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "pro", ["0:QuotaBytes"] = "10" }));
        Assert.Throws<OdinSystemException>(() => Registry(new()
            { ["0:Id"] = "a", ["0:Default"] = "true", ["1:Id"] = "b", ["1:Default"] = "true" }));
        Assert.DoesNotThrow(() => Registry(new() { ["0:Id"] = "legacy", ["0:Default"] = "true" }));
    }

    [Test]
    public void PlanIdsMustBeUniqueAndPresent()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["1:Id"] = "FREE" }));
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["1:QuotaBytes"] = "5" }));
    }

    [TestCase("https://billing.example.com/upgrade", "https://billing.example.com/upgrade?id=frodo.dotyou.cloud")]
    [TestCase(" https://billing.example.com/upgrade?src=app ", "https://billing.example.com/upgrade?src=app&id=frodo.dotyou.cloud")]
    public void TheUpgradeUrlCarriesTheIdentity(string configured, string expected)
    {
        var plans = new HostingPlans(new OdinConfiguration { Registry = Registry([], configured) });
        Assert.That(plans.UpgradeUrlFor(new OdinId("frodo.dotyou.cloud")), Is.EqualTo(expected));
    }

    [Test]
    public void WithoutAnUpgradeUrlThereIsNone()
    {
        Assert.That(Plans([]).UpgradeUrlFor(new OdinId("frodo.dotyou.cloud")), Is.Null);
    }

    [TestCase("http://billing.example.com/upgrade")] // an owner's browser is sent there: https only
    [TestCase("/upgrade")]
    [TestCase("https://billing.example.com/upgrade#plans")] // the fragment would swallow ?id=
    public void AnUpgradeUrlMustBeAbsoluteHttpsWithoutAFragment(string upgradeUrl)
    {
        Assert.Throws<OdinSystemException>(() => Registry([], upgradeUrl));
    }

    [Test]
    public void QuotasCannotBeNegative()
    {
        Assert.Throws<OdinSystemException>(() => Registry(new() { ["0:Id"] = "free", ["0:QuotaBytes"] = "-1" }));
    }
}
