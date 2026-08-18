using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

#nullable enable

public class RegistrationEmailsTest
{
    private static readonly DsRecordData Ds = new(46082, 13, 2, "c8f816a7a575bdb2f997f682aab2653b");

    [Test]
    public void ItShouldOmitTheDnssecSectionByDefault()
    {
        var text = RegistrationEmails.ProvisioningCompletedText("frodo@example.com", "frodo.example.com", "https://link");
        var html = RegistrationEmails.ProvisioningCompletedHtml("frodo.example.com", "https://link");

        Assert.That(text, Does.Not.Contain("DNSSEC"));
        Assert.That(html, Does.Not.Contain("DNSSEC"));
        // The pre-DNSSEC content is intact
        Assert.That(text, Does.Contain("frodo.example.com"));
        Assert.That(text, Does.Contain("https://link"));
    }

    [Test]
    public void ItShouldOmitTheDnssecSectionForAnEmptyRecordList()
    {
        var text = RegistrationEmails.ProvisioningCompletedText("frodo@example.com", "frodo.example.com", "https://link", []);
        Assert.That(text, Does.Not.Contain("DNSSEC"));
    }

    [Test]
    public void ItShouldRenderTheDsRecordInBothEmailBodies()
    {
        var text = RegistrationEmails.ProvisioningCompletedText("frodo@example.com", "frodo.example.com", "https://link", [Ds]);
        var html = RegistrationEmails.ProvisioningCompletedHtml("frodo.example.com", "https://link", [Ds]);

        foreach (var body in new[] { text, html })
        {
            Assert.That(body, Does.Contain("DNSSEC"));
            Assert.That(body, Does.Contain("46082"));
            Assert.That(body, Does.Contain("c8f816a7a575bdb2f997f682aab2653b"));
            // The section explains BOTH placements - registrar (apex) and DNS host (subdomain)
            Assert.That(body, Does.Contain("registrar"));
            Assert.That(body, Does.Contain("DNS host"));
        }
    }
}
