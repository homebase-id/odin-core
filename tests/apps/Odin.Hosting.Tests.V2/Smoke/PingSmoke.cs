using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Smoke;

/// <summary>
/// Minimal sanity check: anonymous V2 endpoint reachable via the in-process pipeline.
/// </summary>
[TestFixture]
public class PingSmoke : V2Fixture
{
    [Test]
    public async Task PingIsReachable()
    {
        using var client = Host.CreateClient();
        var resp = await client.GetAsync($"https://{Identities.Frodo}/api/v2/health/ping");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
