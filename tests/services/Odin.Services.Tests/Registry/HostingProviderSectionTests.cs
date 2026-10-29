using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Configuration;

namespace Odin.Services.Tests.Registry;

public class HostingProviderSectionTests
{
    private static OdinConfiguration.HostingProviderSection Section(Dictionary<string, string?> values)
    {
        var pairs = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            pairs[$"HostingProvider:{key}"] = value;
        }

        return new OdinConfiguration.HostingProviderSection(new ConfigurationBuilder().AddInMemoryCollection(pairs).Build());
    }

    [Test]
    public void WithNothingSetThereIsNoProviderAndSixtyGigabytesEach()
    {
        var section = Section([]);

        Assert.That(section.DisplayName, Is.EqualTo(""));
        Assert.That(section.UpgradeUrl, Is.EqualTo(""));
        Assert.That(section.AutoConsentDomains, Is.Empty);
        Assert.That(section.DefaultQuotaBytes, Is.EqualTo(64_424_509_440));
    }

    [TestCase(null, 64_424_509_440L)]
    [TestCase(1000L, 1000L)]
    public void AnIdentitysOwnQuotaWins_ElseTheDefault(long? stored, long expected)
    {
        Assert.That(Section([]).QuotaFor(stored), Is.EqualTo(expected));
    }

    [Test]
    public void TheDefaultQuotaCanBeSet_ButNotToNothing()
    {
        Assert.That(Section(new() { ["DefaultQuotaBytes"] = "5000" }).DefaultQuotaBytes, Is.EqualTo(5000));
        Assert.Throws<OdinSystemException>(() => Section(new() { ["DefaultQuotaBytes"] = "0" }));
    }

    [TestCase("https://billing.example.com/upgrade",
        "https://billing.example.com/upgrade?id=frodo.dotyou.cloud&host=provisioning.example.com")]
    [TestCase(" https://billing.example.com/upgrade?src=app ",
        "https://billing.example.com/upgrade?src=app&id=frodo.dotyou.cloud&host=provisioning.example.com")]
    public void TheUpgradeUrlCarriesTheIdentityAndItsHost(string configured, string expected)
    {
        var section = Section(new() { ["UpgradeUrl"] = configured });
        Assert.That(section.UpgradeUrlFor("frodo.dotyou.cloud", "provisioning.example.com"), Is.EqualTo(expected));
    }

    [Test]
    public void WithoutAnUpgradeUrlThereIsNone()
    {
        Assert.That(Section([]).UpgradeUrlFor("frodo.dotyou.cloud", "provisioning.example.com"), Is.Null);
    }

    [TestCase("http://billing.example.com/upgrade")] // an owner's browser is sent there: https only
    [TestCase("/upgrade")]
    [TestCase("https://billing.example.com/upgrade#plans")] // the fragment would swallow ?id=
    public void AnUpgradeUrlMustBeAbsoluteHttpsWithoutAFragment(string upgradeUrl)
    {
        Assert.Throws<OdinSystemException>(() => Section(new() { ["UpgradeUrl"] = upgradeUrl }));
    }

    [Test]
    public void AutoConsentDomainsAreLowerCasedAndMustBeDomains()
    {
        var section = Section(new() { ["AutoConsentDomains:0"] = " Invites.RavenHosting.Cloud " });
        Assert.That(section.AutoConsentDomains, Is.EqualTo(new[] { "invites.ravenhosting.cloud" }));

        Assert.That(() => Section(new() { ["AutoConsentDomains:0"] = "not a domain" }), Throws.Exception);
    }
}
