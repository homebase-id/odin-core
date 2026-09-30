#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.BackgroundServices;
using Odin.Services.Configuration;

namespace Odin.Services.Stun;

/// <summary>
/// An open STUN Binding responder (RFC 8489, issue #1838). A WebRTC client sends a Binding request
/// from the UDP socket that will carry media and is told the public IP:port that socket appeared
/// from, which is what ICE needs to offer a server-reflexive candidate. Stateless, unauthenticated
/// by protocol design, no tenant context. Everything that is not a well-formed Binding request is
/// dropped silently: an error reply would be one more reflectable packet.
///
/// The observed address must be the client's, so this cannot sit behind anything that rewrites
/// the UDP source (there is no PROXY protocol for UDP). See docs/stun.md.
/// </summary>
public sealed class StunResponderBackgroundService(
    ILogger<StunResponderBackgroundService> logger,
    OdinConfiguration config)
    : AbstractBackgroundService(logger)
{
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(10);

    private Socket? _socket;

    // Written by the receive loop, read and reset by the stats loop and by StoppedAsync.
    private long _received;
    private long _answered;
    private long _dropped;
    private long _receiveErrors;
    private long _sendErrors;

    /// <summary>The bound endpoint; port 0 in config resolves to the ephemeral port chosen by the OS.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        // A bind failure propagates and fails host startup, like a Kestrel port clash.
        var address = config.Stun.GetBindAddress();
        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // [::] serves both families on one socket; IPv4 peers show up as ::ffff:a.b.c.d.
            // (DualMode is an IPv6-socket property; setting it on an IPv4 socket throws.)
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = address.Equals(IPAddress.IPv6Any);
            }
            socket.Bind(new IPEndPoint(address, config.Stun.Port));
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        LocalEndPoint = (IPEndPoint)socket.LocalEndPoint!;
        logger.LogInformation("STUN responder listening on {endpoint}", LocalEndPoint);
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(ReceiveLoopAsync(stoppingToken), LogStatsLoopAsync(stoppingToken));
    }

    private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("StartingAsync did not bind the socket");

        var rx = new byte[StunBindingCodec.MaxDatagramLength];
        var tx = new byte[StunBindingCodec.MaxResponseLength];
        var anyEndpoint = new IPEndPoint(
            socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        var lastReceiveErrorLogged = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(rx, SocketFlags.None, anyEndpoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows reports an ICMP port-unreachable for an earlier reply this way.
                continue;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize)
            {
                // Windows: datagram larger than the buffer (Linux truncates it, and the header
                // length then disagrees with the datagram, so it is dropped as BadLength).
                Interlocked.Increment(ref _dropped);
                continue;
            }
            catch (SocketException e)
            {
                Interlocked.Increment(ref _receiveErrors);
                var now = DateTimeOffset.UtcNow;
                if (now - lastReceiveErrorLogged > StatsInterval)
                {
                    lastReceiveErrorLogged = now;
                    logger.LogWarning(e, "STUN responder receive failed: {error} (further ones are counted in the stats line)",
                        e.SocketErrorCode);
                }
                await SleepAsync(TimeSpan.FromMilliseconds(100), stoppingToken);
                continue;
            }

            try
            {
                Interlocked.Increment(ref _received);

                var status = StunBindingCodec.TryParseBindingRequest(rx.AsSpan(0, received.ReceivedBytes), out var transactionId);
                if (status != StunParseStatus.BindingRequest)
                {
                    Interlocked.Increment(ref _dropped);
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("STUN responder dropped a {length}-byte datagram: {status}", received.ReceivedBytes, status);
                    }
                    continue;
                }

                var remote = (IPEndPoint)received.RemoteEndPoint;
                // The reply carries the family the request arrived on, so unmap dual-mode's ::ffff:a.b.c.d.
                var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
                var length = StunBindingCodec.WriteBindingSuccess(tx, transactionId, address, remote.Port);

                try
                {
                    await socket.SendToAsync(tx.AsMemory(0, length), SocketFlags.None, received.RemoteEndPoint, stoppingToken);
                    Interlocked.Increment(ref _answered);
                }
                catch (SocketException e)
                {
                    Interlocked.Increment(ref _sendErrors);
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("STUN responder could not reply to {remote}: {error}", remote, e.SocketErrorCode);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // ExecuteWithCatchAllAsync would log this and let the service die; a responder that
                // stays up matters more than one bad packet.
                logger.LogError(e, "STUN responder failed to handle a datagram: {message}", e.Message);
                await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task LogStatsLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(StatsInterval, stoppingToken);
            LogStats();
        }
    }

    private void LogStats()
    {
        var received = Interlocked.Exchange(ref _received, 0);
        var answered = Interlocked.Exchange(ref _answered, 0);
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        var receiveErrors = Interlocked.Exchange(ref _receiveErrors, 0);
        var sendErrors = Interlocked.Exchange(ref _sendErrors, 0);

        if (received + dropped + receiveErrors == 0)
        {
            return;
        }

        logger.LogInformation(
            "STUN responder stats: received={received} answered={answered} dropped={dropped} " +
            "receiveErrors={receiveErrors} sendErrors={sendErrors}",
            received, answered, dropped, receiveErrors, sendErrors);
    }

    protected override Task StoppedAsync(CancellationToken stoppingToken)
    {
        LogStats();
        _socket?.Dispose();
        return Task.CompletedTask;
    }
}
