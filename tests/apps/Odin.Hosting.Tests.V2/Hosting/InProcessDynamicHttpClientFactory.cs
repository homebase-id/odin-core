#nullable enable
using System;
using System.Net.Http;
using Odin.Core.Http;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// An <see cref="IDynamicHttpClientFactory"/> whose clients talk to the in-process
/// <see cref="TestServerHolder.Server"/> rather than the network, so a service that fetches from
/// "some host on the internet" fetches from a test identity instead. The multi-tenant middleware
/// routes on the Host header, so an unknown host gets its 404 the same way an absent site would.
/// </summary>
internal sealed class InProcessDynamicHttpClientFactory(TestServerHolder serverHolder) : IDynamicHttpClientFactory
{
    public HttpClient CreateClient(string remoteHostKey, Action<ClientHandlerConfig>? configure = null)
    {
        var server = serverHolder.Server
            ?? throw new InvalidOperationException("TestServer has not been wired into TestServerHolder yet.");

        // disposeHandler: true -- unlike the peer factory this one is not shared, one handler per client.
        return new HttpClient(server.CreateHandler(), disposeHandler: true);
    }

    public void Dispose()
    {
    }
}
