using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Dns.Health;
using Odin.Services.Security;
using Odin.Services.Security.Health.RiskAnalyzer;
using Odin.Services.Security.Email;

namespace Odin.Services.Tests.Security.Email;

#nullable enable

public class RecoveryEmailsTest
{
    private static readonly OdinId Tenant = (OdinId)"frodo.example.com";

    private static RecoveryInfo ConfiguredInfo()
    {
        return new RecoveryInfo
        {
            IsConfigured = true,
            Email = "frodo@example.com",
            RecoveryRisk = new DealerRecoveryRiskReport
            {
                IsRecoverable = true,
                ValidShardCount = 3,
                MinRequired = 2,
                RiskLevel = RecoveryRiskLevel.Low,
            },
        };
    }

    private static DnssecHealthResult Attention(DnsHealthDnssecStatus status)
    {
        return new DnssecHealthResult
        {
            Status = status,
            ParentZoneSigned = true,
            DsToPublish = [new Odin.Core.Dns.DsRecordData(46082, 13, 2, "c8f816a7a575bdb2f997f682aab2653b")],
        };
    }

    [Test]
    public void ItShouldOmitTheDnssecSectionWhenNothingNeedsAttention()
    {
        var text = RecoveryEmails.FormatRecoveryRiskStatusText(Tenant, ConfiguredInfo());
        var html = RecoveryEmails.FormatRecoveryRiskStatusHtml(Tenant, ConfiguredInfo());

        Assert.That(text, Does.Not.Contain("DNSSEC"));
        Assert.That(html, Does.Not.Contain("DNSSEC"));
    }

    [Test]
    public void ItShouldRenderTheOptionalDsSectionOnDsMissing()
    {
        var attention = Attention(DnsHealthDnssecStatus.DsMissing);
        var text = RecoveryEmails.FormatRecoveryRiskStatusText(Tenant, ConfiguredInfo(), attention);
        var html = RecoveryEmails.FormatRecoveryRiskStatusHtml(Tenant, ConfiguredInfo(), attention);

        foreach (var body in new[] { text, html })
        {
            Assert.That(body, Does.Contain("DNSSEC"));
            Assert.That(body, Does.Contain("Optional"));
            Assert.That(body, Does.Contain("46082"));
            Assert.That(body, Does.Contain("c8f816a7a575bdb2f997f682aab2653b"));
            Assert.That(body, Does.Contain("/owner/security/dns"));
        }
        // Not the alarm wording
        Assert.That(text, Does.Not.Contain("refuse to resolve"));
    }

    [Test]
    public void ItShouldRenderTheAlarmSectionOnDsMismatch()
    {
        var attention = Attention(DnsHealthDnssecStatus.DsMismatch);
        var text = RecoveryEmails.FormatRecoveryRiskStatusText(Tenant, ConfiguredInfo(), attention);
        var html = RecoveryEmails.FormatRecoveryRiskStatusHtml(Tenant, ConfiguredInfo(), attention);

        foreach (var body in new[] { text, html })
        {
            Assert.That(body, Does.Contain("DNSSEC problem"));
            Assert.That(body, Does.Contain("refuse to resolve"));
            Assert.That(body, Does.Contain("46082"));
            Assert.That(body, Does.Contain("/owner/security/dns"));
        }
    }

    [Test]
    public void ItShouldAppendTheDnssecSectionEvenWhenRecoveryIsNotConfigured()
    {
        // The not-configured early-return branch must carry the section too
        var info = new RecoveryInfo { IsConfigured = false, RecoveryRisk = new DealerRecoveryRiskReport() };
        var attention = Attention(DnsHealthDnssecStatus.DsMismatch);

        var text = RecoveryEmails.FormatRecoveryRiskStatusText(Tenant, info, attention);
        var html = RecoveryEmails.FormatRecoveryRiskStatusHtml(Tenant, info, attention);

        Assert.That(text, Does.Contain("DNSSEC problem"));
        Assert.That(html, Does.Contain("DNSSEC problem"));
    }
}
