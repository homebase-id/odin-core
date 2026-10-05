#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Makes chosen peer calls fail, so a test can reproduce what a transient network or server fault does
/// to a flow that spans two identities. One per <see cref="Hosting.OdinHost"/>, so a fixture's faults
/// never reach another fixture's host. Every peer request made through <see cref="TestPeerHttpClientFactory"/>
/// is checked against it.
/// </summary>
public sealed class PeerFaults
{
    private readonly object _lock = new();
    private readonly List<Rule> _rules = [];

    /// <summary>
    /// The next peer request from <paramref name="from"/> to <paramref name="to"/> whose path ends with
    /// <paramref name="pathSuffix"/> is answered with a 500 without reaching the recipient. Once.
    /// </summary>
    public void FailNext(string from, string to, string pathSuffix)
    {
        lock (_lock)
        {
            _rules.Add(new Rule(from, to, pathSuffix));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _rules.Clear();
        }
    }

    internal bool TryTake(string from, string to, string path)
    {
        lock (_lock)
        {
            var i = _rules.FindIndex(rule =>
                string.Equals(rule.From, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.To, to, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(rule.PathSuffix, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                return false;
            }

            _rules.RemoveAt(i);
            return true;
        }
    }

    private sealed record Rule(string From, string To, string PathSuffix);

    /// <summary>Checks each outbound peer request against <see cref="PeerFaults"/> before it reaches the server.</summary>
    internal sealed class Handler(HttpMessageHandler inner, PeerFaults faults, string from, string to)
        : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (faults.TryTake(from, to, request.RequestUri?.AbsolutePath ?? ""))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
