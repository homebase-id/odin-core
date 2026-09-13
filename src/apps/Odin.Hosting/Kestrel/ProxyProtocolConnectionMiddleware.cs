#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using Serilog.Events;

namespace Odin.Hosting.Kestrel;

/// <summary>
/// Kestrel connection middleware that requires a PROXY protocol header from a trusted proxy and
/// rewrites the connection's remote endpoint from it, so everything above (TLS, HTTP,
/// <c>HttpContext.Connection.RemoteIpAddress</c>) sees the real client. Must be registered
/// before <c>UseHttps</c>: the header precedes the TLS ClientHello on the wire.
///
/// Connections from a peer outside <c>trustedProxies</c>, and connections that do not start with
/// a valid header, are closed. Silently accepting either would let any client claim any source
/// address, which is worse than the address loss the header is there to fix.
/// </summary>
public static class ProxyProtocolConnectionMiddleware
{
    private static readonly TimeSpan HeaderReadTimeout = TimeSpan.FromSeconds(5);

    public static ListenOptions UseProxyProtocol(this ListenOptions listenOptions, IReadOnlyList<IPNetwork> trustedProxies)
    {
        ArgumentNullException.ThrowIfNull(trustedProxies);
        if (trustedProxies.Count == 0)
        {
            throw new ArgumentException("PROXY protocol requires at least one trusted proxy network", nameof(trustedProxies));
        }

        var logger = Log.ForContext(typeof(ProxyProtocolConnectionMiddleware));

        listenOptions.Use(next => async context =>
        {
            var peer = (context.RemoteEndPoint as IPEndPoint)?.Address;
            if (peer == null || !IsTrusted(peer, trustedProxies))
            {
                logger.Warning("PROXY protocol: rejecting connection from untrusted peer {Peer} on {Local}",
                    peer, context.LocalEndPoint);
                context.Abort(new ConnectionAbortedException("PROXY protocol: untrusted peer"));
                return;
            }

            var header = await ReadHeaderAsync(context, logger);
            if (header == null)
            {
                context.Abort(new ConnectionAbortedException("PROXY protocol: missing or invalid header"));
                return;
            }

            if (header.HasAddresses)
            {
                context.RemoteEndPoint = header.Source;
                context.LocalEndPoint = header.Destination;
            }

            await next(context);
        });

        return listenOptions;
    }

    private static async Task<ProxyProtocolHeader?> ReadHeaderAsync(ConnectionContext context, ILogger logger)
    {
        var input = context.Transport.Input;
        using var timeout = new CancellationTokenSource(HeaderReadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, context.ConnectionClosed);
        var bytesReceived = 0L;

        try
        {
            while (true)
            {
                var result = await input.ReadAsync(linked.Token);
                var buffer = result.Buffer;
                bytesReceived = Math.Max(bytesReceived, buffer.Length);
                var status = ProxyProtocolParser.TryParse(buffer, out var header, out var consumed);

                if (status == ProxyProtocolParseStatus.Success)
                {
                    input.AdvanceTo(consumed);
                    return header;
                }

                if (status == ProxyProtocolParseStatus.Invalid || result.IsCompleted)
                {
                    input.AdvanceTo(buffer.Start);
                    LogNoHeader(logger, context, bytesReceived,
                        status == ProxyProtocolParseStatus.Invalid ? "invalid header" : "connection closed before header");
                    return null;
                }

                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // ConnectionClosed is linked in as well, so say which of the two actually fired rather
            // than blaming the timeout for a peer that hung up.
            LogNoHeader(logger, context, bytesReceived,
                timeout.IsCancellationRequested
                    ? $"no header within {HeaderReadTimeout}"
                    : "connection closed before header");
            return null;
        }
        catch (ConnectionResetException)
        {
            // A peer that resets instead of closing. Without this the exception escapes into
            // Kestrel, which logs it as an unhandled connection error.
            LogNoHeader(logger, context, bytesReceived, "connection reset before header");
            return null;
        }
    }

    /// <summary>
    /// A connection that proved the port accepts and then said nothing is a load balancer health
    /// probe, not a malformed client: an L4 TCP monitor cannot send a PROXY header, and on some
    /// balancers the monitor cannot be aimed at a different port. Logging every probe at Warning
    /// drowned the log stream (issue #1731 measured it at 71% of all records over 24h). Verbose
    /// keeps it recoverable on demand - and it has to be Verbose rather than Debug, because the
    /// minimum level here is Debug by default (appsettings.json), so Debug would still ship.
    ///
    /// A peer that sent SOMETHING that was not a valid header stays at Warning: that is a
    /// misconfigured proxy or a client on the wrong port, which is worth seeing.
    /// </summary>
    private static void LogNoHeader(ILogger logger, ConnectionContext context, long bytesReceived, string reason)
    {
        var level = bytesReceived == 0 ? LogEventLevel.Verbose : LogEventLevel.Warning;
        logger.Write(level, "PROXY protocol: {Reason} from {Peer} after {BytesReceived} bytes",
            reason, context.RemoteEndPoint, bytesReceived);
    }

    public static bool IsTrusted(IPAddress peer, IReadOnlyList<IPNetwork> trustedProxies)
    {
        // Kestrel's dual-stack sockets report IPv4 peers as ::ffff:a.b.c.d
        var candidates = peer.IsIPv4MappedToIPv6 ? new[] { peer, peer.MapToIPv4() } : new[] { peer };
        foreach (var network in trustedProxies)
        {
            foreach (var candidate in candidates)
            {
                if (network.BaseAddress.AddressFamily == candidate.AddressFamily && network.Contains(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
