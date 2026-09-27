#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// How the authorize endpoint reports a failure: by sending the browser back to the relying party
/// with an error code, the way the consent page's cancel and the home-site login already do, and
/// the way the reference client already expects.
/// </summary>
/// <remarks>
/// Only once the redirect target is trusted. A redirect to an address nobody has vouched for would
/// make the endpoint an open redirector, so until the redirect URI has passed the checks the
/// success path applies -- parses, host equals the client id for a domain, the app is registered
/// for an app -- a bad request is still answered with a 400 at the identity. After that point the
/// relying party gets <c>?error=code&amp;state=...</c> on its own redirect URI, with the query it
/// sent kept and its state echoed so it can match the answer to the request.
/// <para>
/// Driven over raw HTTP with the owner's cookie, like <c>YouAuthIntegrationTests</c>, because the
/// Location header is the subject.
/// </para>
/// </remarks>
[TestFixture]
public class YouAuthErrorRedirectTests : V2Fixture
{
    private const string HttpsPort = "8443";

    [Test]
    public async Task ADomainClientMissingItsPublicKeyIsSentBackWithAnError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var thirdParty = "amazoom.org";

        var response = await Authorize(owner, new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientType = ClientType.domain,
            PublicKey = "",
            State = "state-4711",
            RedirectUri = $"https://{thirdParty}/callback"
        });

        var location = AssertRedirectsTo(response, $"https://{thirdParty}/callback");
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
        Assert.That(location[YouAuthDefaults.State], Is.EqualTo("state-4711"), "the relying party matches the answer to its request by state");
    }

    [Test]
    public async Task TheRelyingPartysOwnQueryIsKept()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var thirdParty = "amazoom.org";

        var response = await Authorize(owner, new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientType = ClientType.domain,
            PublicKey = "",
            State = "s",
            RedirectUri = $"https://{thirdParty}/callback?session=abc"
        });

        var location = AssertRedirectsTo(response, $"https://{thirdParty}/callback");
        Assert.That(location["session"], Is.EqualTo("abc"), "what the relying party put on its redirect URI comes back with the error");
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
    }

    [Test]
    public async Task ARevokedAppIsSentBackWithAnError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = await owner.Admin.RegisterBareApp();
        var revoke = await owner.RefitFor<IRefitAppRegistration>().RevokeApp(new GetAppRequest { AppId = appId });
        Assert.That(revoke.IsSuccessStatusCode, Is.True, $"RevokeApp failed: {revoke.StatusCode}");

        var response = await Authorize(owner, new YouAuthAuthorizeRequest
        {
            ClientId = appId.ToString(),
            ClientType = ClientType.app,
            PermissionRequest = OdinSystemSerializer.Serialize(new YouAuthAppParameters
            {
                AppId = appId.ToString(),
                AppName = "Revoked App",
                ClientFriendly = "Firefox | macOS",
                DrivesParam = "[]"
            }),
            PublicKey = NewPublicKey(),
            State = "s",
            RedirectUri = "https://app.example.org/callback"
        });

        var location = AssertRedirectsTo(response, "https://app.example.org/callback");
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorAppRevoked),
            "a registered app is a trusted redirect target, and revoked is a reason the app can act on");
    }

    [Test]
    public async Task ARedirectHostThatIsNotTheDomainClientIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await Authorize(owner, new YouAuthAuthorizeRequest
        {
            ClientId = "amazoom.org",
            ClientType = ClientType.domain,
            PublicKey = NewPublicKey(),
            State = "s",
            RedirectUri = "https://somewhere-else.org/callback"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "nobody has vouched for that host, so it must not be redirected to, not even with an error");
    }

    [Test]
    public async Task AnUnregisteredAppWithABadRequestIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await Authorize(owner, new YouAuthAuthorizeRequest
        {
            ClientId = Guid.NewGuid().ToString(),
            ClientType = ClientType.app,
            PermissionRequest = "this is not json",
            PublicKey = NewPublicKey(),
            State = "s",
            RedirectUri = "https://app.example.org/callback"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "an app nobody has registered has no redirect target the identity can trust");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static string NewPublicKey()
    {
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        return new EccFullKeyData(privateKey, EccKeySize.P384, 1).PublicKeyJwkBase64Url();
    }

    private async Task<HttpResponseMessage> Authorize(OwnerSession owner, YouAuthAuthorizeRequest payload)
    {
        var uri = new UriBuilder($"https://{owner.Identity.DomainName}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
        {
            Query = payload.ToQueryString()
        }.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, owner.Token.ToString()).ToString() } },
        };

        return await Host.CreateClient().SendAsync(request);
    }

    /// <summary>
    /// The response is a redirect to the relying party's redirect URI; returns its query for the
    /// caller to assert on.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, string> AssertRedirectsTo(HttpResponseMessage response, string expectedUriWithoutQuery)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a failure after the redirect target is trusted goes back to the relying party");

        var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(location.Split('?')[0], Is.EqualTo(expectedUriWithoutQuery));

        return YouAuthTestHelper.ParseQueryString(location);
    }
}
