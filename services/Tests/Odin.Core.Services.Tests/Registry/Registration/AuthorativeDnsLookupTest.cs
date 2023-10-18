using System;
using System.Linq;
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
    [TestCase(".", "a.root-servers.net")]
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
    public async Task ItShouldLookupTheAuthorativeNameServer(string domain, string expectedAuthorityNameserver)
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object);
        var result = await lookup.Lookup(domain);

        Assert.That(result, Is.EqualTo(expectedAuthorityNameserver));
    }

    [Test]
    public void ItShouldThrowOnBadDomain()
    {
        var loggerMock = new Mock<ILogger<AuthorativeDnsLookup>>();
        var lookup = new AuthorativeDnsLookup(loggerMock.Object);
        Assert.ThrowsAsync<AuthorativeDnsLookupException>(async () => await lookup.Lookup("not a domain"));
    }

}