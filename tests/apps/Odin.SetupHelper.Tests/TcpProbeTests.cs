using Odin.Core.Cache;

namespace Odin.SetupHelper.Tests;

public class TcpProbeTests
{
    [Test]
    public async Task ItShouldConnectToValidPort()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("example.com", "80");
        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: example.com:80"));
    }
    
    [Test]
    public async Task ItShouldCacheConnectionResults()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("example.com", "80");
        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: example.com:80"));
        
        (success, message) = await tcpProbe.ProbeAsync("example.com", "80");
        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: example.com:80 [cache hit]"));
    }
    
    //
    
    [Test]
    public async Task ItShouldGiveUpConnectionToBlockedPort()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("example.com", "22");
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Failed to connect to TCP: example.com:22"));
    }
    
}