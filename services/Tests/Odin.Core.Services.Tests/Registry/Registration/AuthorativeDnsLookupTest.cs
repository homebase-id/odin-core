using System.Net;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Services.Registry.Registration;

namespace Odin.Core.Services.Tests.Registry.Registration;


public class AuthorativeDnsLookupTest
{
    [Test]
    public async Task ItShouldGetTheRootServers()
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object, new LookupClient());
        var result = await lookup.LookupRootAuthority();
        Assert.That(result.AuthorativeDomain, Is.EqualTo(""));
        Assert.That(result.AuthorativeNameServer, Is.EqualTo("a.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("a.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("h.root-servers.net"));
        Assert.That(result.NameServers, Does.Contain("m.root-servers.net"));
    }

    [Test, Explicit]
    [TestCase("", "", "a.root-servers.net", "a.root-servers.net", 1)]
    [TestCase(".", "", "a.root-servers.net", "a.root-servers.net", 1)]
    [TestCase("com", "com", "a.gtld-servers.net", "a.gtld-servers.net", 1)]
    [TestCase("dk", "dk", "b.nic.dk", "b.nic.dk", 1)]
    [TestCase("id", "id", "b.dns.id", "b.dns.id", 1)]
    [TestCase("example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("www.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("aslikdjaslidjsakldj.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("foo.bar.baz.www.example.com", "example.com", "ns.icann.org", "a.iana-servers.net", 1)]
    [TestCase("sebbarg.dk", "sebbarg.dk", "ns1.sebbarg.dk", "ns1.sebbarg.dk", 1)]
    [TestCase("www.sebbarg.dk", "sebbarg.dk", "ns1.sebbarg.dk", "ns1.sebbarg.dk", 1)]
    [TestCase("seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("michael.seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("capi.michael.seifert.page", "seifert.page", "dns1.registrar-servers.com", "dns1.registrar-servers.com", 1)]
    [TestCase("bishwajeetparhi.dev", "bishwajeetparhi.dev", "ns1.yay.com", "ns1.yay.com", 1)]
    [TestCase("capi.bishwajeetparhi.dev", "bishwajeetparhi.dev", "ns1.yay.com", "ns1.yay.com", 1)]
    [TestCase("stefcoenen.be", "stefcoenen.be", "phil.ns.cloudflare.com", "phil.ns.cloudflare.com", 1)]
    [TestCase("www.stefcoenen.be", "stefcoenen.be", "phil.ns.cloudflare.com", "phil.ns.cloudflare.com", 1)]
    [TestCase("yagni.dk", "yagni.dk", "adele.ns.cloudflare.com", "adele.ns.cloudflare.com", 1)]
    [TestCase("www.yagni.dk", "yagni.dk", "adele.ns.cloudflare.com", "adele.ns.cloudflare.com", 1)]
    [TestCase("id.pub", "id.pub","ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("dns.id.pub", "id.pub", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("admin.dominion.id", "dominion.id", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("dominion.id", "dominion.id", "ns1.id.pub", "ns1.id.pub", 1)]
    [TestCase("martin.vonhaller.info", "vonhaller.info", "ns01.one.com", "ns01.one.com", 1)]
    [TestCase("wrwerakujsdjhaskdjashdaskjdhxcmvnuj.com", "com", "a.gtld-servers.net", "a.gtld-servers.net", 1)]
    [TestCase("ertertakujsdjhaskdjashdaskjdhxcmvnuj.id", "id", "b.dns.id", "b.dns.id", 1)]
    [TestCase("not a domain", "", "", "", 0)]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "", "", "", 0)]
    public async Task ItShouldLookupAuthorativeStuff(
        string domain,
        string expectedAuthorityDomain,
        string expectedAuthorityNameserver,
        string expectedOtherNameServer,
        int expectedMinNameServers)
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object, new LookupClient());
        var result = await lookup.LookupDomainAuthority(domain);
        Assert.That(result.Exception, Is.Null);
        Assert.That(result.AuthorativeDomain, Is.EqualTo(expectedAuthorityDomain));
        Assert.That(result.AuthorativeNameServer, Is.EqualTo(expectedAuthorityNameserver));
        Assert.That(result.NameServers.Count, Is.GreaterThanOrEqualTo(expectedMinNameServers));
        Assert.That(result.NameServers, Is.Empty.Or.Contain(expectedOtherNameServer));
    }

    //

    [Test, Explicit]
    [TestCase("", "")]
    [TestCase(".", "")]
    [TestCase("com", "com")]
    [TestCase("example.com", "example.com")]
    [TestCase("www.example.com", "example.com")]
    [TestCase("aslikdjaslidjsakldj.example.com", "example.com")]
    [TestCase("foo.bar.baz.www.example.com", "example.com")]
    [TestCase("yagni.dk", "yagni.dk")]
    [TestCase("www.yagni.dk", "yagni.dk")]
    [TestCase("sebbarg.net", "sebbarg.net")]
    [TestCase("foo.sebbarg.net", "sebbarg.net")]
    [TestCase("not a domain", "")]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "")]
    public async Task ItShouldLookupZoneApexForTheDomain(string domain, string expectedZoneApex)
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object, new LookupClient());
        var result = await lookup.LookupZoneApex(domain);

        Assert.That(result, Is.EqualTo(expectedZoneApex));
    }

    //

}

