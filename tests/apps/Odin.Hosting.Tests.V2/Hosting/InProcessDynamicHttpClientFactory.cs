#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Http;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Routes an <see cref="IDynamicHttpClientFactory"/> fetch into the in-process server; a host in
/// <see cref="CannedDocuments"/> is answered with that JSON instead, so a made-up site can publish
/// a document without being a tenant. An unknown host gets the server's 404, like an absent site.
/// </summary>
internal sealed class InProcessDynamicHttpClientFactory(TestServerHolder serverHolder) : IDynamicHttpClientFactory
{
    /// <summary>Host to JSON body, served with 200 and application/json for any path on that host.</summary>
    public static readonly ConcurrentDictionary<string, string> CannedDocuments = new(StringComparer.OrdinalIgnoreCase);

    public HttpClient CreateClient(string remoteHostKey, Action<ClientHandlerConfig>? configure = null)
    {
        var server = serverHolder.Server
            ?? throw new InvalidOperationException("TestServer has not been wired into TestServerHolder yet.");

        // disposeHandler: true -- unlike the peer factory this one is not shared, one handler per client.
        return new HttpClient(new CannedOrServer(server.CreateHandler()), disposeHandler: true);
    }

    public void Dispose()
    {
    }

    private sealed class CannedOrServer(HttpMessageHandler server) : DelegatingHandler(server)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host;
            if (host != null && CannedDocuments.TryGetValue(host, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
