#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

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

        try
        {
            while (true)
            {
                var result = await input.ReadAsync(linked.Token);
                var buffer = result.Buffer;
                var status = ProxyProtocolParser.TryParse(buffer, out var header, out var consumed);

                if (status == ProxyProtocolParseStatus.Success)
                {
                    input.AdvanceTo(consumed);
                    return header;
                }

                if (status == ProxyProtocolParseStatus.Invalid || result.IsCompleted)
                {
                    input.AdvanceTo(buffer.Start);
                    logger.Warning("PROXY protocol: {Reason} from {Peer}",
                        status == ProxyProtocolParseStatus.Invalid ? "invalid header" : "connection closed before header",
                        context.RemoteEndPoint);
                    return null;
                }

                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            logger.Warning("PROXY protocol: no header within {Timeout} from {Peer}", HeaderReadTimeout, context.RemoteEndPoint);
            return null;
        }
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
