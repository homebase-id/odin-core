using System.Collections.Generic;
using NUnit.Framework;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

/// <summary>
/// The autoconfig XML and the app's setup screen must describe the SAME server. They are read
/// by different people in different places — a client that configures itself, and a human
/// typing into a mail app that has no autoconfig support — and disagreeing by one port number
/// would send the second group to a connection that hangs with no error.
/// </summary>
public class MailClientSettingsTest
{
    [Test]
    public void ItShouldUseTheFirstMailHostForBothDirections()
    {
        var settings = MailClientSettings.For(["mx1.example.com", "mx2.example.com"], "mail@tenant.example");

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings!.IncomingHost, Is.EqualTo("mx1.example.com"));
        Assert.That(settings.OutgoingHost, Is.EqualTo("mx1.example.com"));

        // The FULL address, not the local part - both servers authenticate with it.
        Assert.That(settings.Username, Is.EqualTo("mail@tenant.example"));
    }

    [Test]
    public void ItShouldUseImplicitTlsPortsNotStartTls()
    {
        var settings = MailClientSettings.For(["mx1.example.com"], "mail@tenant.example")!;

        // 465 rather than 587, and SSL rather than STARTTLS. A client told the wrong pairing
        // fails to connect at all - which is how the first submission attempt was lost.
        Assert.That(settings.IncomingPort, Is.EqualTo(993));
        Assert.That(settings.OutgoingPort, Is.EqualTo(465));
        Assert.That(settings.IncomingSocketType, Is.EqualTo("SSL"));
        Assert.That(settings.OutgoingSocketType, Is.EqualTo("SSL"));
    }

    [Test]
    public void ItShouldReturnNullWhenTheHostPublishesNoMailHosts()
    {
        // Tenant mail off, or configured without MxNodes: show nothing rather than a form
        // with a blank server name in it.
        Assert.That(MailClientSettings.For([], "mail@tenant.example"), Is.Null);
        Assert.That(MailClientSettings.For([""], "mail@tenant.example"), Is.Null);
    }

    [Test]
    public void ItShouldMatchWhatTheAutoconfigXmlPublishes()
    {
        // The guard that makes the shared definition worth having: if these ever diverge, a
        // client that configures itself and a human reading the screen end up on different
        // servers, and only one of them gets an error.
        var hosts = new List<string> { "mx1.example.com" };
        var settings = MailClientSettings.For(hosts, "mail@tenant.example")!;
        var xml = MailAutoconfig.BuildXml("tenant.example", hosts);

        Assert.That(xml, Does.Contain($"<hostname>{settings.IncomingHost}</hostname>"));
        Assert.That(xml, Does.Contain($"<port>{settings.IncomingPort}</port>"));
        Assert.That(xml, Does.Contain($"<port>{settings.OutgoingPort}</port>"));
        Assert.That(xml, Does.Contain($"<socketType>{settings.IncomingSocketType}</socketType>"));
    }
}
