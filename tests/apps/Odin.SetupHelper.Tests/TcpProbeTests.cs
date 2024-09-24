using Odin.Core.Cache;
using Odin.Hosting.Cli;

namespace Odin.SetupHelper.Tests;

public class TcpProbeTests
{
    //
    
    [Test]
    public async Task ItShouldGiveUpConnectionToBlockedPort()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("example.com", "22");
        Assert.That(success, Is.False);
        Assert.That(message, Does.StartWith("Failed to connect to TCP: example.com:22"));
    }
    
    //
    
    [Test]
    public async Task ItShouldConnectToHttpPortAndGetExpectedResponse()
    {
        var cts = new CancellationTokenSource();
        var listenTask = DockerSetup.TcpListen(8080, cts.Token);
        
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", "8080");
        
        await cts.CancelAsync();
        var (connected, error) = await listenTask;

        Assert.That(connected, Is.True);
        Assert.That(error, Is.Null);
        
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: 127.0.0.1:8080"));
        Assert.That(success, Is.True);
    }
    
    //
    
    [Test]
    public async Task ItShouldConnectToHttpsPortAndGetExpectedResponse()
    {
        var cts = new CancellationTokenSource();
        var listenTask = DockerSetup.TcpListen(8443, cts.Token);
        
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", "8443");
        
        await cts.CancelAsync();
        var (connected, error) = await listenTask;

        Assert.That(connected, Is.True);
        Assert.That(error, Is.Null);
        
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: 127.0.0.1:8443"));
        Assert.That(success, Is.True);
    }
    
    //
    
    [Test]
    public async Task ItShouldCacheConnectionResults()
    {
        var cts = new CancellationTokenSource();
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);

        {
            using var listenTask = DockerSetup.TcpListen(8443, cts.Token);
            var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", "8443");
        
            await cts.CancelAsync();
            var (connected, error) = await listenTask;

            Assert.That(connected, Is.True);
            Assert.That(error, Is.Null);
        
            Assert.That(message, Is.EqualTo("Successfully connected to TCP: 127.0.0.1:8443"));
            Assert.That(success, Is.True);
        }
        
        {
            var listenTask = DockerSetup.TcpListen(443, cts.Token);
            var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", "8443");
        
            await cts.CancelAsync();
            var (connected, error) = await listenTask;

            Assert.That(connected, Is.True);
            Assert.That(error, Is.Null);
        
            Assert.That(message, Is.EqualTo("Successfully connected to TCP: 127.0.0.1:8443 [cache hit]"));
            Assert.That(success, Is.True);
        }
    }
    
    //
    
    [Test]
    public async Task ItShouldErrorOnUnexpectedResponse()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("example.com", "80");
        Assert.That(message, Is.EqualTo("Successfully connected to TCP: example.com:80, but did not get the expected response"));
        Assert.That(success, Is.False);
    }
    
    //
}