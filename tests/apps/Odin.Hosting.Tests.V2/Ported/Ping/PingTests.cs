using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Ported.Ping;

/// <summary>
/// Port of <c>_V2/Tests/Ping/PingTests</c>. Confirms the anonymous /health/ping endpoint reaches
/// the V2 pipeline and that unknown /api/ + /api/v2/ routes return 404 (and not 401 / 500 from a
/// fall-through handler). The anonymous-ping case overlaps with <c>Smoke/PingSmoke</c>; this file
/// keeps both originals together for 1:1 traceability against the source.
/// </summary>
[TestFixture]
public class PingTests : V2Fixture
{
    protected override bool ResetBetweenTests => false;

    [Test]
    public async Task ItShouldPingAnonymously()
    {
        using var client = Host.CreateClient();
        var resp = await client.GetAsync($"https://{Identities.Frodo}/api/v2/health/ping");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ItShould404OnUnknownApiRoute()
    {
        using var client = Host.CreateClient();

        var resp = await client.GetAsync($"https://{Identities.Frodo}/api/404");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        resp = await client.GetAsync($"https://{Identities.Frodo}/api/v2/404");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
