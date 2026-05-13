using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Smoke;

/// <summary>
/// Confirms one <see cref="V2Fixture"/> host can serve multiple identities and that callers
/// for different tenants are isolated. The router-by-Host-header premise of the framework
/// rests on this passing.
/// </summary>
[TestFixture]
public class MultiIdentitySmoke : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];
    protected override bool ResetBetweenTests => false;

    [Test]
    public async Task PingResolvesPerTenant()
    {
        using var client = Host.CreateClient();

        var frodoResp = await client.GetAsync($"https://{Identities.Frodo}/api/v2/health/ping");
        var samResp = await client.GetAsync($"https://{Identities.Sam}/api/v2/health/ping");

        Assert.That(frodoResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(samResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var frodoBody = await frodoResp.Content.ReadFromJsonAsync<PingReply>();
        var samBody = await samResp.Content.ReadFromJsonAsync<PingReply>();
        Assert.That(frodoBody!.Identity, Is.EqualTo(Identities.Frodo));
        Assert.That(samBody!.Identity, Is.EqualTo(Identities.Sam));
    }

    [Test]
    public async Task EachIdentityHasIsolatedOwnerLogin()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        Assert.That(frodo.Token.Id, Is.Not.EqualTo(sam.Token.Id), "tokens must be distinct per tenant");
        Assert.That((await frodo.Auth.VerifyToken()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await sam.Auth.VerifyToken()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private sealed class PingReply
    {
        public string Identity { get; set; } = "";
    }
}
