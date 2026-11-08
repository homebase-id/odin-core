#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// The local dev server (Odin.Hosting on :4444, *.dotyou.cloud) as the target of the V2 test clients, for a local
/// fixture tool rather than a test: it reaches only the identities named here, and never another host.
/// </summary>
public sealed class DevServer(int port, IReadOnlySet<string> identities) : ITestSync
{
    public int Port { get; } = port;

    /// <summary>The address of <paramref name="identity"/> on the dev server; refuses any identity not named</summary>
    public Uri BaseFor(string identity)
    {
        if (!identities.Contains(identity) || !identity.EndsWith(".dotyou.cloud", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{identity} isn't a dev identity this tool may reach");
        }

        return new Uri($"https://{identity}:{Port}/");
    }

    /// <summary>A handler for the dev server: its certificates are the repo's dev ones, trusted for these hosts alone</summary>
    public HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        UseCookies = false,
        SslOptions =
        {
            RemoteCertificateValidationCallback = (sender, _, _, _) =>
                sender is System.Net.Security.SslStream { TargetHostName: { } host } && identities.Contains(host)
        }
    };

    // The dev server runs its own outbox; a drain only gives it time
    public Task DrainOutboxAsync(CancellationToken cancellationToken = default) => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

    public Task<InboxStatus> ProcessInboxAsync(TargetDrive drive, int batchSize = 100, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Process an inbox through the caller's own HTTP endpoint");

    public Task<bool> IsOutboxEmptyAsync(TargetDrive drive) => throw new NotSupportedException();
}
