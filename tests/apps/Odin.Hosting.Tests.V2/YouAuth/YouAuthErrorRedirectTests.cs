#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// How the authorize endpoint reports a failure: back to the relying party with an error code once
/// its redirect target is trusted, a 400 at the identity before that. The rule and its reason are
/// on <c>YouAuthUnifiedController.Authorize</c>; the step numbers in the test names are the flow
/// diagram's (<c>docs/youauth-unified-authorization.md</c>): [030] is the request arriving and being
/// refused, [060] is a failure reported to the relying party. Driven over raw HTTP with the owner's
/// cookie, because the Location header is the subject.
/// </summary>
[TestFixture]
public class YouAuthErrorRedirectTests : V2Fixture
{
    private const string Domain = "amazoom.org";
    private const string DomainCallback = $"https://{Domain}/callback";
    private const string AppCallback = "https://app.example.org/callback";

    [Test]
    public async Task YouAuth060_ADomainClientMissingItsPublicKeyIsSentBackWithAnError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest();
        request.PublicKey = "";
        request.State = "state-4711";

        var location = AssertRedirectsTo(await Authorize(owner, request), DomainCallback);
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
        Assert.That(location[YouAuthDefaults.State], Is.EqualTo("state-4711"), "the relying party matches the answer to its request by state");
    }

    [Test]
    public async Task YouAuth060_TheRelyingPartysOwnQueryIsKept()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest();
        request.PublicKey = "";
        request.RedirectUri = $"{DomainCallback}?session=abc";

        var location = AssertRedirectsTo(await Authorize(owner, request), DomainCallback);
        Assert.That(location["session"], Is.EqualTo("abc"), "what the relying party put on its redirect URI comes back with the error");
    }

    [Test]
    public async Task YouAuth060_ARevokedAppIsSentBackWithAnError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = await owner.Admin.RegisterBareApp();
        var revoke = await owner.RefitFor<IRefitOwnerAppRegistration>().RevokeApp(new GetAppRequest { AppId = appId });
        Assert.That(revoke.IsSuccessStatusCode, Is.True, $"RevokeApp failed: {revoke.StatusCode}");

        var request = AppRequest(appId, OdinSystemSerializer.Serialize(new YouAuthAppParameters
        {
            AppId = appId.ToString(),
            AppName = "Revoked App",
            ClientFriendly = "Firefox | macOS",
            DrivesParam = "[]"
        }));

        var location = AssertRedirectsTo(await Authorize(owner, request), AppCallback);
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorAppRevoked),
            "a registered app is a trusted redirect target, and revoked is a reason the app can act on");
    }

    [Test]
    public async Task YouAuth030_ARedirectHostThatIsNotTheDomainClientIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest();
        request.RedirectUri = "https://somewhere-else.org/callback";

        var response = await Authorize(owner, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "nobody has vouched for that host, so it must not be redirected to, not even with an error");
    }

    [Test]
    public async Task YouAuth030_AnUnregisteredAppWithABadRequestIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await Authorize(owner, AppRequest(Guid.NewGuid(), "this is not json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "an app nobody has registered has no redirect target the identity can trust");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A well-formed domain-client request; a test breaks the one field it is about.
    /// </summary>
    private static YouAuthAuthorizeRequest DomainRequest() => new()
    {
        ClientId = Domain,
        ClientType = ClientType.domain,
        PublicKey = NewPublicKey(),
        State = "s",
        RedirectUri = DomainCallback
    };

    private static YouAuthAuthorizeRequest AppRequest(Guid appId, string permissionRequest) => new()
    {
        ClientId = appId.ToString(),
        ClientType = ClientType.app,
        PermissionRequest = permissionRequest,
        PublicKey = NewPublicKey(),
        State = "s",
        RedirectUri = AppCallback
    };

    private static string NewPublicKey()
    {
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        return new EccFullKeyData(privateKey, EccKeySize.P384, 1).PublicKeyJwkBase64Url();
    }

    private async Task<HttpResponseMessage> Authorize(OwnerSession owner, YouAuthAuthorizeRequest payload)
    {
        var uri = new UriBuilder($"https://{owner.Identity.DomainName}{OwnerApiPathConstants.YouAuthV1Authorize}")
        {
            Query = payload.ToQueryString()
        }.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Headers = { { "Cookie", new Cookie(OwnerAuthConstants.CookieName, owner.Token.ToString()).ToString() } },
        };

        using var client = Host.CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>
    /// The response is a redirect to the relying party's redirect URI; returns its query for the
    /// caller to assert on.
    /// </summary>
    private static Dictionary<string, string> AssertRedirectsTo(HttpResponseMessage response, string expectedUriWithoutQuery)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a failure after the redirect target is trusted goes back to the relying party");

        var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(location.Split('?')[0], Is.EqualTo(expectedUriWithoutQuery));

        return YouAuthTestHelper.ParseQueryString(location);
    }
}
