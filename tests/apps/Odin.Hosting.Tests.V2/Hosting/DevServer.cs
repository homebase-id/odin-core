#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// The local dev server (Odin.Hosting on :4444, *.dotyou.cloud) as the target of the V2 test clients, for a local
/// fixture tool rather than a test: it reaches only the identities named here, and never another host.
/// </summary>
public sealed class DevServer : IOdinTarget, ITestSync
{
    private readonly int _port;
    private readonly IReadOnlySet<string> _identities;

    // One connection pool for every client.  It dials this machine whatever DNS answers for the names, so the certificate
    // check only has to keep to the names: the dev certificates' private keys are in the repo anyway
    private readonly SocketsHttpHandler _handler;

    public DevServer(int port, IReadOnlySet<string> identities)
    {
        _port = port;
        _identities = identities;
        _handler = new SocketsHttpHandler
        {
            UseCookies = false,
            // As the in-process host's client: a test reads a redirect, never follows it
            AllowAutoRedirect = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var (host, connectPort) = (context.DnsEndPoint.Host, context.DnsEndPoint.Port);
                if (!identities.Contains(host) || connectPort != port)
                {
                    throw new InvalidOperationException($"{host}:{connectPort} isn't the dev server this tool may reach");
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions =
            {
                RemoteCertificateValidationCallback = (sender, _, _, _) =>
                    sender is System.Net.Security.SslStream { TargetHostName: { } host } && identities.Contains(host)
            }
        };
    }

    public HttpMessageHandler CreateHandler() => new NonDisposingPassthroughHandler(_handler);

    /// <summary>The address of <paramref name="identity"/> on the dev server; refuses any identity not named</summary>
    public Uri BaseFor(string identity)
    {
        if (!_identities.Contains(identity) || !identity.EndsWith(".dotyou.cloud", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{identity} isn't a dev identity this tool may reach");
        }

        return new Uri($"https://{identity}:{_port}/");
    }

    // The dev server runs its own outbox and inbox: a tool waits for what they deliver, as a client would
    public ITestSync GetTestSync(string identity) => this;

    private const string NoTestSync = "The dev server runs its own outbox and inbox; wait for what they deliver instead";

    public Task DrainOutboxAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException(NoTestSync);

    public Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NoTestSync);

    public Task<bool> IsOutboxEmptyAsync(TargetDrive drive) => throw new NotSupportedException(NoTestSync);
}
