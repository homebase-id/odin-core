#nullable enable
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Covers <see cref="V2Fixture.ConfigOverrides"/> — the hook that lets a fixture boot its host
/// with settings the production code reads at startup.
///
/// The two fixtures below are deliberately a pair: they set opposing values for the same key and
/// run in parallel (<c>ParallelScope.Fixtures</c>). If overrides ever leaked between hosts — the
/// failure mode of the process-wide environment variables the old framework used — one of them
/// would see the other's value and fail.
/// </summary>
public class ConfigOverrideAppliedTests : V2Fixture
{
    protected override bool ResetBetweenTests => false;

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Email:TenantMail:Enabled"] = "true",
            // List settings bind by index. The env form (MxNodes__0) does not work here.
            ["Email:TenantMail:MxNodes:0"] = "mx1.dotyou.cloud",
            ["Email:TenantMail:MxNodes:1"] = "mx2.dotyou.cloud",
            ["Email:TenantMail:SpfIncludeTarget"] = "spf.dotyou.cloud",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc@dotyou.cloud",
            ["Email:TenantMail:TlsReportEmail"] = "tlsrpt@dotyou.cloud",
        };

    [Test]
    public void ScalarOverrideReachesTheBootedHost()
    {
        var config = Host.Server.Services.GetRequiredService<OdinConfiguration>();
        Assert.That(config.Email.TenantMail.Enabled, Is.True);
        Assert.That(config.Email.TenantMail.SpfIncludeTarget, Is.EqualTo("spf.dotyou.cloud"));
    }

    [Test]
    public void ListOverrideBindsByIndex()
    {
        var config = Host.Server.Services.GetRequiredService<OdinConfiguration>();
        Assert.That(config.Email.TenantMail.MxNodes, Is.EqualTo(new[] { "mx1.dotyou.cloud", "mx2.dotyou.cloud" }));
    }
}

/// <summary>
/// The control half of the pair: no overrides, so the defaults must survive — including while
/// <see cref="ConfigOverrideAppliedTests"/> boots a host with the same key set to true.
/// </summary>
public class ConfigOverrideDefaultTests : V2Fixture
{
    protected override bool ResetBetweenTests => false;

    [Test]
    public void DefaultsAreUntouchedByAnotherFixturesOverrides()
    {
        var config = Host.Server.Services.GetRequiredService<OdinConfiguration>();
        Assert.That(config.Email.TenantMail.Enabled, Is.False);
    }
}
