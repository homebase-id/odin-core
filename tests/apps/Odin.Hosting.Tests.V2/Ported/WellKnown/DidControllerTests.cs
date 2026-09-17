using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.V2.Ported.WellKnown;

/// <summary>
/// Port of <c>Fingering/DidControllerTests</c>. An unauthenticated caller reads the tenant's
/// <c>did:web</c> document from <c>/.well-known/did.json</c>.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original built its <c>WebScaffold</c> in <c>[SetUp]</c> rather than
/// <c>[OneTimeSetUp]</c>, so it booted a whole Kestrel host per test method. The fixture here boots
/// one in-process host for the class.</item>
/// <item>Frodo is pinned rather than left to the fixture default because the assertion spells out
/// <c>did:web:frodo.dotyou.cloud</c>.</item>
/// <item>Kept separate from <see cref="WebfingerControllerTests"/>: the two originals differ in
/// endpoint and assertions, which the README says is not a caller difference and so not a merge.</item>
/// </list>
/// </remarks>
[TestFixture]
public class DidControllerTests : V2Fixture
{
    [Test]
    public async Task ItShouldGetDidWeb()
    {
        using var apiClient = Host.CreateAnonymousClient(Identities.Frodo);
        var response = await apiClient.GetAsync("/.well-known/did.json");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = OdinSystemSerializer.Deserialize<DidWebResponse>(await response.Content.ReadAsStringAsync());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo($"did:web:{Identities.Frodo}"));
        Assert.That(result.VerificationMethod, Is.Not.Empty);
    }
}
