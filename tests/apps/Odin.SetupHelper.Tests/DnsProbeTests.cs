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
    
    [Test]
    [Retry(3)]
    public async Task ItShouldGetTheDomainAuthority()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (authority, message) = await dnsProbe.LookupDomainAuthority("id.homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo($"Authoritative name server found for id.homebase.id"));
    }
    
    [Test]
    [Retry(3)]
    public async Task ItShouldGetTheDomainAuthorityUsingCache()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (authority, message) = await dnsProbe.LookupDomainAuthority("id.homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo("Authoritative name server found for id.homebase.id"));
        
        (authority, message) = await dnsProbe.LookupDomainAuthority("id.homebase.id");
        Assert.That(authority, Is.EqualTo("dns1.registrar-servers.com"));
        Assert.That(message, Is.EqualTo("Authoritative name server found for id.homebase.id [cache hit]"));
        
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

    [Test]
    [Retry(3)]
    public async Task ItShouldResolveCnameDomainToIpWithCache()
    {
        var dnsProbe = _serviceProvider.GetRequiredService<DnsProbe>();

        var (ip, message) = await dnsProbe.ResolveIpAsync("capi.id.homebase.id");
        Assert.That(ip, Is.EqualTo("135.181.203.146"));
        Assert.That(message, Is.EqualTo("Resolved capi.id.homebase.id to 135.181.203.146"));
        
        (ip, message) = await dnsProbe.ResolveIpAsync("capi.id.homebase.id");
        Assert.That(ip, Is.EqualTo("135.181.203.146"));
        Assert.That(message, Is.EqualTo("Resolved capi.id.homebase.id to 135.181.203.146 [cache hit]"));
    }
}