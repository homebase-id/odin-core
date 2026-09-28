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
    /// The next <paramref name="times"/> peer requests from <paramref name="from"/> to <paramref name="to"/>
    /// whose path ends with <paramref name="pathSuffix"/> are answered with <paramref name="status"/>
    /// without reaching the recipient.
    /// </summary>
    public void FailNext(string from, string to, string pathSuffix, int times = 1,
        HttpStatusCode status = HttpStatusCode.InternalServerError)
    {
        lock (_lock)
        {
            _rules.Add(new Rule(from, to, pathSuffix, status) { Remaining = times });
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _rules.Clear();
        }
    }

    internal bool TryTake(string from, string to, string path, out HttpStatusCode status)
    {
        lock (_lock)
        {
            foreach (var rule in _rules)
            {
                if (rule.Remaining > 0 &&
                    string.Equals(rule.From, from, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.To, to, StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(rule.PathSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    rule.Remaining--;
                    status = rule.Status;
                    return true;
                }
            }
        }

        status = default;
        return false;
    }

    private sealed record Rule(string From, string To, string PathSuffix, HttpStatusCode Status)
    {
        public int Remaining { get; set; }
    }

    /// <summary>Checks each outbound peer request against <see cref="PeerFaults"/> before it reaches the server.</summary>
    internal sealed class Handler(HttpMessageHandler inner, PeerFaults faults, string from, string to)
        : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (faults.TryTake(from, to, request.RequestUri?.AbsolutePath ?? "", out var status))
            {
                return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
