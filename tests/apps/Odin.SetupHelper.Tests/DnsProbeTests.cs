using System.Net;
using System.Net.Sockets;
using DnsClient;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Cache;
using Odin.Core.Dns;

namespace Odin.SetupHelper.Tests;

public class DnsProbeTests
{
    private ServiceProvider _serviceProvider;
    
    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGenericMemoryCache, GenericMemoryCache>();
        services.AddSingleton<ILookupClient, LookupClient>();
        services.AddSingleton<IAuthoritativeDnsLookup, AuthoritativeDnsLookup>();
        services.AddSingleton<DnsProbe>();
        _serviceProvider = services.BuildServiceProvider();
    }    
   
    [TearDown]
    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
    
    // Live DNS: homebase.id stays with its registrar's name servers. Not id.homebase.id, which moved to our own
    // (ns1.id.pub) on 2026-10-01 - a name whose delegation we change cannot anchor this test
    [Test]
    [Retry(3)]
    public async Task ItShouldGetTheDomainAuthority()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (authority, message) = await dnsProbe.LookupDomainAuthority("homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo($"Authoritative name server found for homebase.id"));
    }
    
    [Test]
    [Retry(3)]
    public async Task ItShouldGetTheDomainAuthorityUsingCache()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (authority, message) = await dnsProbe.LookupDomainAuthority("homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo("Authoritative name server found for homebase.id"));
        
        (authority, message) = await dnsProbe.LookupDomainAuthority("homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo("Authoritative name server found for homebase.id [cache hit]"));
        
    }
    
    [Test]
    [Retry(3)]
    public async Task ItShouldSaySomethingOnLookupDomainAuthorityError()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();
        var (authority, message) = await dnsProbe.LookupDomainAuthority("id.homebase.id.asdsadasd.asdasdasdas.d.asd");
        
        Assert.That(authority, Is.Empty);
        Assert.That(message, Is.EqualTo("No authoritative name server found for id.homebase.id.asdsadasd.asdasdasdas.d.asd"));
    }

    [Test]
    [Retry(3)]
    public async Task ItShouldResolveARecordDomainToIpWithCache()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (ip, message) = await dnsProbe.ResolveIpAsync("homebase.id");
        Assert.That(ip, Is.EqualTo("75.2.60.5"));
        Assert.That(message, Is.EqualTo("Resolved homebase.id to 75.2.60.5"));
        
        (ip, message) = await dnsProbe.ResolveIpAsync("homebase.id");
        Assert.That(ip, Is.EqualTo("75.2.60.5"));
        Assert.That(message, Is.EqualTo("Resolved homebase.id to 75.2.60.5 [cache hit]"));
    }

    // Live DNS: capi.id.homebase.id is a CNAME to whichever host serves it, so the address moves with our hosting
    // (135.181.203.146 until 2026-10-01). Assert that the CNAME resolves to an IPv4 address and that the second
    // lookup is a cache hit for the same address, not which address it is
    [Test]
    [Retry(3)]
    public async Task ItShouldResolveCnameDomainToIpWithCache()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (ip, message) = await dnsProbe.ResolveIpAsync("capi.id.homebase.id");
        Assert.That(IPAddress.TryParse(ip, out var address), Is.True, $"not an IP address: '{ip}'");
        Assert.That(address!.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        Assert.That(message, Is.EqualTo($"Resolved capi.id.homebase.id to {ip}"));
        
        var (cachedIp, cachedMessage) = await dnsProbe.ResolveIpAsync("capi.id.homebase.id");
        Assert.That(cachedIp, Is.EqualTo(ip));
        Assert.That(cachedMessage, Is.EqualTo($"Resolved capi.id.homebase.id to {ip} [cache hit]"));
    }
}