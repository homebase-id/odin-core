using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

public class MailAutoconfigTest
{
    [Test]
    public void ItShouldEmitOneServerPairPerMailHost()
    {
        var xml = MailAutoconfig.BuildXml("frodo.dotyou.cloud", ["mx1.id.pub", "mx2.id.pub"]);

        Assert.That(xml, Does.StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>"));

        var document = XDocument.Parse(xml);
        var clientConfig = document.Root!;
        Assert.That(clientConfig.Name.LocalName, Is.EqualTo("clientConfig"));
        Assert.That(clientConfig.Attribute("version")!.Value, Is.EqualTo("1.1"));

        var provider = clientConfig.Element("emailProvider")!;
        Assert.That(provider.Attribute("id")!.Value, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(provider.Element("domain")!.Value, Is.EqualTo("frodo.dotyou.cloud"));

        var incoming = provider.Elements("incomingServer").ToList();
        Assert.That(incoming.Select(s => s.Element("hostname")!.Value), Is.EqualTo(new[] { "mx1.id.pub", "mx2.id.pub" }));
        Assert.That(incoming.All(s => s.Attribute("type")!.Value == "imap"));
        Assert.That(incoming.All(s => s.Element("port")!.Value == "993"));
        Assert.That(incoming.All(s => s.Element("socketType")!.Value == "SSL"));
        Assert.That(incoming.All(s => s.Element("username")!.Value == "%EMAILADDRESS%"));

        var outgoing = provider.Elements("outgoingServer").ToList();
        Assert.That(outgoing.Select(s => s.Element("hostname")!.Value), Is.EqualTo(new[] { "mx1.id.pub", "mx2.id.pub" }));
        Assert.That(outgoing.All(s => s.Attribute("type")!.Value == "smtp"));
        Assert.That(outgoing.All(s => s.Element("port")!.Value == "465"));
    }

    [Test]
    public void ItShouldEscapeXmlSensitiveCharacters()
    {
        // Domains can't really contain these, but the builder must never produce broken XML
        var xml = MailAutoconfig.BuildXml("a&b<c>.example", ["mx.example"]);
        Assert.DoesNotThrow(() => XDocument.Parse(xml));
    }
}
