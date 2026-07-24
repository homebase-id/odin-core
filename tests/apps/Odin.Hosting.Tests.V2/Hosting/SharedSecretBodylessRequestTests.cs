#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// <c>SharedSecretEncryptionMiddleware.ShouldDecryptRequest</c> skips decryption when the request
/// carries no body at all. That exemption used to be POST-only, so a bodyless PUT/PATCH fell through
/// to <c>DecryptRequest</c>, which tried to parse an empty stream as a <c>SharedSecretEncryptedPayload</c>
/// and returned 400 <c>sharedSecretEncryptionIsInvalid</c> — making bodyless PUT endpoints
/// (e.g. accept-incoming-connection-request) uncallable unless the client sent an encrypted empty payload.
/// </summary>
[TestFixture]
public class SharedSecretBodylessRequestTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task BodylessPut_IsNotTreatedAsAnEncryptedPayload()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var sendReq = await new UniversalCircleNetworkRequestsApiClient(sam.Identity, sam.Factory)
            .SendConnectionRequest(frodo.Identity);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {sendReq.StatusCode}");

        // Raw client rather than a Refit wrapper: this must be a genuinely bodyless PUT — no
        // Content-Length, no serialized (and therefore encrypted) payload of any kind.
        using var client = frodo.Factory.CreateHttpClient(frodo.Identity, out _);
        var response = await client.PutAsync(
            $"{UnifiedApiRouteConstants.Connections}/requests/incoming/{sam.Identity}", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            $"expected 204, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
