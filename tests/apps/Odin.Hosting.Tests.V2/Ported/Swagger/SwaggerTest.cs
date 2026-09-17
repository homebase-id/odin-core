using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Ported.Swagger;

/// <summary>
/// Port of <c>SwaggerTest</c> (repo root of the V1 test project). The two unauthenticated
/// diagnostic surfaces: the generated swagger document and the health probe.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>No caller matrix in the original and none here — both paths are anonymous.</item>
/// <item>The original pinned Samwise and initialized that identity; neither assertion depends on
/// which tenant answers, so this uses the fixture default. <c>ResetBetweenTests</c> is left on
/// (default) even though neither test writes, because the baseline snapshot the fixture takes is
/// what materializes the tenant in the first place.</item>
/// <item>The original's per-test <c>ClearLogEvents</c> / <c>AssertLogEvents</c> tear-down is now the
/// <see cref="V2Fixture"/> default, so the log-event invariant is carried rather than dropped.</item>
/// <item>Swagger is only mapped in the Development environment; the fast host boots with
/// <c>ASPNETCORE_ENVIRONMENT=Development</c>, as the V1 scaffold did.</item>
/// </list>
/// </remarks>
[TestFixture]
public class SwaggerTest : V2Fixture
{
    [Test]
    public async Task TestSwaggerIsUp()
    {
        using var client = Host.CreateAnonymousClient(PrimaryIdentity);
        var result = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TestHealthzEndpoint()
    {
        using var client = Host.CreateAnonymousClient(PrimaryIdentity);
        var result = await client.GetAsync("/healthz");
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await result.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Healthy"));
    }
}
