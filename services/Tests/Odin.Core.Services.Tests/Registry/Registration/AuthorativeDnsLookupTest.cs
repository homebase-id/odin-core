using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Services.Registry.Registration;

namespace Odin.Core.Services.Tests.Registry.Registration;

public class AuthorativeDnsLookupTest
{
    [Test]
    [TestCase("", "")]
    [TestCase(".", "")]
    [TestCase("com", "a.gtld-servers.net")]
    [TestCase("dk", "b.nic.dk")]
    [TestCase("example.com", "ns.icann.org")]
    [TestCase("www.example.com", "ns.icann.org")]
    [TestCase("foo.bar.baz.www.example.com", "ns.icann.org")]
    [TestCase("sebbarg.dk", "ns1.sebbarg.dk")]
    [TestCase("www.sebbarg.dk", "ns1.sebbarg.dk")]
    [TestCase("seifert.page", "dns1.registrar-servers.com")]
    [TestCase("michael.seifert.page", "dns1.registrar-servers.com")]
    [TestCase("capi.michael.seifert.page", "dns1.registrar-servers.com")]
    [TestCase("bishwajeetparhi.dev", "ns1.yay.com")]
    [TestCase("capi.bishwajeetparhi.dev", "ns1.yay.com")]
    [TestCase("stefcoenen.be", "phil.ns.cloudflare.com")]
    [TestCase("www.stefcoenen.be", "phil.ns.cloudflare.com")]
    [TestCase("yagni.dk", "adele.ns.cloudflare.com")]
    [TestCase("www.yagni.dk", "adele.ns.cloudflare.com")]
    [TestCase("id.pub", "ns1.id.pub")]
    [TestCase("dns.id.pub", "ns1.id.pub")]
    [TestCase("dominion.id", "ns1.id.pub")]
    [TestCase("admin.dominion.id", "ns1.id.pub")]
    [TestCase("not a domain", "")]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "")]
    public async Task ItShouldLookupTheAuthorativeNameServer(string domain, string expectedAuthorityNameserver)
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object);
        var result = await lookup.LookupNameServer(domain);

        Assert.That(result, Is.EqualTo(expectedAuthorityNameserver));
    }

    //

    [Test]
    [TestCase("", "")]
    [TestCase(".", "")]
    [TestCase("com", "com")]
    [TestCase("example.com", "example.com")]
    [TestCase("www.example.com", "example.com")]
    [TestCase("foo.bar.baz.www.example.com", "example.com")]
    [TestCase("yagni.dk", "yagni.dk")]
    [TestCase("www.yagni.dk", "yagni.dk")]
    [TestCase("not a domain", "")]
    [TestCase("asdasdsdasd.asdasdasd.asdasdasdqeqwe.dvxcvxcv", "")]
    public async Task ItShouldLookupZoneApexForTheDomain(string domain, string expectedZoneApex)
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object);
        var result = await lookup.LookupZoneApex(domain);

        Assert.That(result, Is.EqualTo(expectedZoneApex));
    }


}