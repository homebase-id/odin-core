#nullable enable
using Microsoft.AspNetCore.TestHost;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Holds the <see cref="TestServer"/> reference between two phases of host startup so peer-routing
/// services registered before <c>host.StartAsync</c> can resolve it lazily at first request. The
/// <see cref="OdinHost"/> populates <see cref="Server"/> right after <c>host.GetTestServer()</c>
/// becomes available; any peer call before that throws (see <c>TestPeerHttpClientFactory</c>).
/// </summary>
internal sealed class TestServerHolder
{
    public TestServer? Server { get; set; }
}
