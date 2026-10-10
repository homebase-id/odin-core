using System.Net;
using System.Net.Sockets;
using Odin.Core.Cache;
using Odin.Hosting.Cli.Commands;

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
        Assert.That(message, Does.StartWith("Failed to connect to example.com:22"));
    }
    
    //
    
    [Test]
    public async Task ItShouldConnectToListeningPortAndGetExpectedResponse()
    {
        var (listenTask, port) = Listen();

        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", port.ToString());

        var (connected, error) = await listenTask;

        Assert.That(connected, Is.True, error);
        Assert.That(error, Is.Null);

        Assert.That(message, Is.EqualTo($"Successfully connected to 127.0.0.1:{port}"));
        Assert.That(success, Is.True);
    }

    //

    [Test]
    public async Task ItShouldCacheConnectionResults()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);

        var (listenTask, port) = Listen();
        var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", port.ToString());
        var (connected, error) = await listenTask;

        Assert.That(connected, Is.True, error);
        Assert.That(message, Is.EqualTo($"Successfully connected to 127.0.0.1:{port}"));
        Assert.That(success, Is.True);

        // Nothing listens any more, so only the cache can answer this
        (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", port.ToString());

        Assert.That(message, Is.EqualTo($"Successfully connected to 127.0.0.1:{port} [cache hit]"));
        Assert.That(success, Is.True);
    }

    //

    // A port the kernel picked, bound before the probe runs. A fixed port can be taken by
    // another process; 38080 and 38443 lie in Linux's ephemeral range, where any outgoing
    // connection on the machine may be using them.
    private static (Task<(bool connected, string? error)> listenTask, int port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var listenTask = DockerSetup.TcpListen(listener, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        return (listenTask, port);
    }

    //

    [Test]
    [Retry(3)]
    public async Task ItShouldErrorOnUnexpectedResponse()
    {
        var cache = new GenericMemoryCache();
        var tcpProbe = new TcpProbe(cache);
        var (success, message) = await tcpProbe.ProbeAsync("www.google.com", "443");
        Assert.That(message, Is.EqualTo("Successfully connected to www.google.com:443, but did not get the expected response"));
        Assert.That(success, Is.False);
    }
    
    //
}