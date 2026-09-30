#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Services.Background;
using Odin.Services.Configuration;
using Odin.Services.Stun;
using Odin.Test.Helpers.Logging;
using Odin.Test.Helpers.Stun;
using Serilog.Events;

namespace Odin.Services.Tests.Stun;

/// <summary>
/// Real UDP round trips against the responder on an ephemeral loopback port, started through
/// the same <see cref="BackgroundServiceManager"/> the host uses. Nothing here touches 3478.
/// </summary>
public class StunResponderBackgroundServiceTests
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoReplyWindow = TimeSpan.FromMilliseconds(300);

    private static readonly byte[] TransactionId =
        [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c];

    private ILifetimeScope _container = null!;
    private LogEventMemoryStore _logEventMemoryStore = null!;
    private IBackgroundServiceManager _manager = null!;

    [SetUp]
    public void Setup()
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        _container = null!;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_container != null!)
        {
            await _manager.ShutdownAsync();
            await _container.DisposeAsync();
        }
    }

    private async Task<StunResponderBackgroundService> StartResponder(string bindAddress)
    {
        var config = new OdinConfiguration
        {
            Stun = new OdinConfiguration.StunSection
            {
                Enabled = true,
                BindAddress = bindAddress,
                Port = 0,
            }
        };

        var builder = new ContainerBuilder();
        builder.RegisterType<CorrelationUniqueIdGenerator>().As<ICorrelationIdGenerator>().SingleInstance();
        builder.RegisterType<CorrelationContext>().As<ICorrelationContext>().SingleInstance();
        builder.RegisterType<StickyHostnameGenerator>().As<IStickyHostnameGenerator>().SingleInstance();
        builder.RegisterType<StickyHostname>().As<IStickyHostname>().SingleInstance();
        builder.RegisterInstance(TestLogFactory.CreateConsoleLogger<BackgroundServiceManager>(_logEventMemoryStore))
            .As<ILogger<BackgroundServiceManager>>();
        builder.RegisterInstance(TestLogFactory.CreateConsoleLogger<StunResponderBackgroundService>(_logEventMemoryStore))
            .As<ILogger<StunResponderBackgroundService>>();
        builder.RegisterInstance(config).AsSelf().SingleInstance();
        builder.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), "system"))
            .As<IBackgroundServiceManager>()
            .SingleInstance();
        builder.RegisterType<StunResponderBackgroundService>().InstancePerDependency();
        _container = builder.Build();

        _manager = _container.Resolve<IBackgroundServiceManager>();
        var service = await _manager.StartAsync<StunResponderBackgroundService>();

        Assert.That(service.LocalEndPoint, Is.Not.Null, "StartingAsync must publish the bound endpoint");
        Assert.That(service.LocalEndPoint!.Port, Is.Not.Zero, $"expected an ephemeral port, bound {service.LocalEndPoint}");
        return service;
    }

    private static UdpClient ClientOn(IPAddress address) => new(new IPEndPoint(address, 0));

    private static byte[] BareBindingRequest() => StunTestMessages.BareBindingRequest(TransactionId);

    private static async Task<byte[]> SendAndReceive(UdpClient client, IPEndPoint responder, byte[] datagram)
    {
        await client.SendAsync(datagram, datagram.Length, responder);
        var result = await client.ReceiveAsync().WaitAsync(ReplyTimeout);
        Assert.That(result.RemoteEndPoint.Port, Is.EqualTo(responder.Port),
            $"reply came from {result.RemoteEndPoint}, responder is {responder}");
        return result.Buffer;
    }

    private static async Task AssertNoReply(UdpClient client, string because)
    {
        // Cancellable, so no orphaned receive is left behind to swallow a later, wanted reply.
        using var cts = new CancellationTokenSource(NoReplyWindow);
        UdpReceiveResult reply;
        try
        {
            reply = await client.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Assert.Fail($"{because}, but got {reply.Buffer.Length} bytes from {reply.RemoteEndPoint}: " +
                    Convert.ToHexString(reply.Buffer));
    }

    [Test]
    public async Task IPv4_RoundTrip_ReportsTheClientsOwnEndpoint()
    {
        var service = await StartResponder("127.0.0.1");
        using var client = ClientOn(IPAddress.Loopback);

        var reply = await SendAndReceive(client, service.LocalEndPoint!,
            BareBindingRequest());

        Assert.That(reply.Length, Is.EqualTo(32), $"reply was {Convert.ToHexString(reply)}");
        Assert.That(reply[8..20], Is.EqualTo(TransactionId), "transaction id must be echoed");
        Assert.That(StunTestMessages.TryReadXorMappedAddress(reply, out var mapped), Is.True,
            $"could not decode {Convert.ToHexString(reply)}");
        Assert.That(mapped, Is.EqualTo(client.Client.LocalEndPoint),
            $"responder said {mapped}, client socket is {client.Client.LocalEndPoint}");
    }

    [Test]
    public async Task IPv6_RoundTrip_ReportsIPv6Family()
    {
        Assume.That(Socket.OSSupportsIPv6, "no IPv6 on this machine");

        var service = await StartResponder("::1");
        using var client = ClientOn(IPAddress.IPv6Loopback);

        var reply = await SendAndReceive(client, service.LocalEndPoint!,
            BareBindingRequest());

        Assert.That(reply.Length, Is.EqualTo(44), $"reply was {Convert.ToHexString(reply)}");
        Assert.That(reply[25], Is.EqualTo(0x02), $"family byte was 0x{reply[25]:X2}");
        Assert.That(StunTestMessages.TryReadXorMappedAddress(reply, out var mapped), Is.True,
            $"could not decode {Convert.ToHexString(reply)}");
        Assert.That(mapped, Is.EqualTo(client.Client.LocalEndPoint),
            $"responder said {mapped}, client socket is {client.Client.LocalEndPoint}");
    }

    [Test]
    public async Task DualStack_IPv4Client_GetsAnIPv4Answer()
    {
        Assume.That(Socket.OSSupportsIPv6, "no IPv6 on this machine, '*' would bind IPv4-only");

        var service = await StartResponder("*");
        Assert.That(service.LocalEndPoint!.Address, Is.EqualTo(IPAddress.IPv6Any),
            $"'*' should bind dual-stack on [::], bound {service.LocalEndPoint}");
        using var client = ClientOn(IPAddress.Loopback);

        var reply = await SendAndReceive(client, new IPEndPoint(IPAddress.Loopback, service.LocalEndPoint.Port),
            BareBindingRequest());

        Assert.That(reply.Length, Is.EqualTo(32), $"an IPv4 client must get an IPv4 answer, reply was {Convert.ToHexString(reply)}");
        Assert.That(StunTestMessages.TryReadXorMappedAddress(reply, out var mapped), Is.True,
            $"could not decode {Convert.ToHexString(reply)}");
        Assert.That(mapped, Is.EqualTo(client.Client.LocalEndPoint),
            $"responder said {mapped}, client socket is {client.Client.LocalEndPoint}");
    }

    [Test]
    public async Task Garbage_IsIgnored_AndTheNextRequestIsStillAnswered()
    {
        var service = await StartResponder("127.0.0.1");
        using var client = ClientOn(IPAddress.Loopback);
        var responder = service.LocalEndPoint!;

        await client.SendAsync("hello"u8.ToArray(), 5, responder);
        var indication = BareBindingRequest();
        indication[1] = 0x11;
        await client.SendAsync(indication, indication.Length, responder);
        await AssertNoReply(client, "garbage and an indication must be dropped");

        var reply = await SendAndReceive(client, responder, BareBindingRequest());

        Assert.That(reply.Length, Is.EqualTo(32), $"reply was {Convert.ToHexString(reply)}");
        await AssertNoReply(client, "exactly one reply per request");
    }

    [Test]
    public async Task Stop_ClosesTheSocket()
    {
        var service = await StartResponder("127.0.0.1");
        var responder = service.LocalEndPoint!;
        using var client = ClientOn(IPAddress.Loopback);
        _ = await SendAndReceive(client, responder, BareBindingRequest());

        await _manager.StopAsync(nameof(StunResponderBackgroundService));

        // The send is fine (UDP); there is just nobody home any more.
        try
        {
            await client.SendAsync(BareBindingRequest(), 20, responder);
            await AssertNoReply(client, "a stopped responder must not answer");
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
        {
            // Windows reports the ICMP port-unreachable this way; that is also "nobody home".
        }

        var errors = _logEventMemoryStore.GetLogEvents()[LogEventLevel.Error];
        Assert.That(errors, Is.Empty, $"stop must be clean, got: {string.Join(" | ", errors)}");
    }
}
