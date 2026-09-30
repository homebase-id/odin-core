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

    // Windows: a UDP socket reports ICMP port-unreachable for an earlier send as a
    // ConnectionReset on the next receive. This IOControl code turns that off.
    private const int SioUdpConnReset = unchecked((int)0x9800000C);

    private Socket? _socket;

    private long _received;
    private long _answered;
    private long _notStun;
    private long _notBinding;
    private long _badLength;
    private long _oversized;
    private long _receiveErrors;
    private long _sendErrors;

    /// <summary>The bound endpoint; port 0 in config resolves to the ephemeral port chosen by the OS.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        // A bind failure propagates and fails host startup, like a Kestrel port clash.
        _socket = Bind(config.Stun.GetBindAddress(), config.Stun.Port);
        LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
        logger.LogInformation("STUN responder listening on {endpoint}", LocalEndPoint);
        return Task.CompletedTask;
    }

    private Socket Bind(IPAddress bindAddress, int port)
    {
        if (config.Stun.BindAddress == "*" && bindAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // "*": dual-stack so one socket serves both families. Fall back to IPv4-only where the
            // OS refuses (net.ipv6.bindv6only=1, or a container without IPv6).
            try
            {
                return BindOne(IPAddress.IPv6Any, port, dualMode: true);
            }
            catch (SocketException e)
            {
                logger.LogWarning(
                    "STUN responder could not bind dual-stack on [::]:{port} ({error}); falling back to IPv4 only",
                    port, e.SocketErrorCode);
                return BindOne(IPAddress.Any, port, dualMode: false);
            }
        }

        return BindOne(bindAddress, port, dualMode: false);
    }

    private static Socket BindOne(IPAddress address, int port, bool dualMode)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (dualMode)
            {
                socket.DualMode = true;
            }
            if (OperatingSystem.IsWindows())
            {
                socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }
            socket.Bind(new IPEndPoint(address, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
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
        // Reused across iterations: the SocketAddress overloads do not allocate an endpoint per packet.
        var from = new SocketAddress(socket.AddressFamily);
        var endpointTemplate = new IPEndPoint(
            socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        var lastReceiveErrorLogged = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await socket.ReceiveFromAsync(rx, SocketFlags.None, from, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows ICMP port-unreachable for an earlier reply; nothing to do with this receive.
                continue;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize)
            {
                // Windows: datagram larger than the buffer (Linux truncates instead, see BadLength).
                Interlocked.Increment(ref _oversized);
                continue;
            }
            catch (SocketException) when (stoppingToken.IsCancellationRequested)
            {
                break;
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

                var status = StunBindingCodec.TryParseBindingRequest(rx.AsSpan(0, n), out var transactionId);
                if (status != StunParseStatus.BindingRequest)
                {
                    Count(status);
                    logger.LogTrace("STUN responder dropped a {length}-byte datagram: {status}", n, status);
                    continue;
                }

                var remote = (IPEndPoint)endpointTemplate.Create(from);
                // A dual-mode socket reports IPv4 peers as ::ffff:a.b.c.d; the reply carries the
                // family the request arrived on.
                var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
                var length = StunBindingCodec.WriteBindingSuccess(tx, transactionId, address, remote.Port);

                try
                {
                    // 'from' is still in the socket's own (possibly mapped) form, which is what SendTo needs.
                    await socket.SendToAsync(tx.AsMemory(0, length), SocketFlags.None, from, stoppingToken);
                    Interlocked.Increment(ref _answered);
                    logger.LogTrace("STUN responder told {remote} its mapped address", remote);
                }
                catch (SocketException e)
                {
                    Interlocked.Increment(ref _sendErrors);
                    logger.LogTrace("STUN responder could not reply to {remote}: {error}", remote, e.SocketErrorCode);
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

    private void Count(StunParseStatus status)
    {
        switch (status)
        {
            case StunParseStatus.NotStun:
                Interlocked.Increment(ref _notStun);
                break;
            case StunParseStatus.NotBindingRequest:
                Interlocked.Increment(ref _notBinding);
                break;
            case StunParseStatus.BadLength:
                Interlocked.Increment(ref _badLength);
                break;
            case StunParseStatus.Oversized:
                Interlocked.Increment(ref _oversized);
                break;
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

    /// <summary>Logs and resets the counters. Public so a stop can flush the last window.</summary>
    public void LogStats()
    {
        var received = Interlocked.Exchange(ref _received, 0);
        var answered = Interlocked.Exchange(ref _answered, 0);
        var notStun = Interlocked.Exchange(ref _notStun, 0);
        var notBinding = Interlocked.Exchange(ref _notBinding, 0);
        var badLength = Interlocked.Exchange(ref _badLength, 0);
        var oversized = Interlocked.Exchange(ref _oversized, 0);
        var receiveErrors = Interlocked.Exchange(ref _receiveErrors, 0);
        var sendErrors = Interlocked.Exchange(ref _sendErrors, 0);

        if (received + oversized + receiveErrors == 0)
        {
            return;
        }

        logger.LogInformation(
            "STUN responder stats: received={received} answered={answered} notStun={notStun} " +
            "notBinding={notBinding} badLength={badLength} oversized={oversized} " +
            "receiveErrors={receiveErrors} sendErrors={sendErrors}",
            received, answered, notStun, notBinding, badLength, oversized, receiveErrors, sendErrors);
    }

    protected override Task StoppedAsync(CancellationToken stoppingToken)
    {
        LogStats();
        _socket?.Dispose();
        _socket = null;
        LocalEndPoint = null;
        return Task.CompletedTask;
    }
}
