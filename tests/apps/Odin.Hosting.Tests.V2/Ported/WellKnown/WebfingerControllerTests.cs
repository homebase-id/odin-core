using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.V2.Ported.WellKnown;

/// <summary>
/// Port of <c>Fingering/WebfingerControllerTests</c>. An unauthenticated caller reads the tenant's
/// WebFinger document from <c>/.well-known/webfinger</c>.
/// </summary>
/// <remarks>
/// Checked port. Same shape as <see cref="DidControllerTests"/>: the original built its
/// <c>WebScaffold</c> in <c>[SetUp]</c>, so it paid a full Kestrel boot per test method. Frodo is
/// pinned because the assertion spells out <c>acct:@frodo.dotyou.cloud</c>.
/// </remarks>
[TestFixture]
public class WebfingerControllerTests : V2Fixture
{
    [Test]
    public async Task ItShouldGetWebfinger()
    {
        using var apiClient = Host.CreateAnonymousClient(Identities.Frodo);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://{Identities.Frodo}/.well-known/webfinger");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = OdinSystemSerializer.Deserialize<WebFingerResponse>(await response.Content.ReadAsStringAsync());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Subject, Is.EqualTo($"acct:@{Identities.Frodo}"));
    }
}
