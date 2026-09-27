#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.YouAuth;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

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

        var request = DomainRequest(Domain, DomainCallback);
        request.PublicKey = "";
        request.State = "state-4711";

        var location = AssertRedirectsTo(await AuthorizeAsync(Host, owner, request), DomainCallback);
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
        Assert.That(location[YouAuthDefaults.State], Is.EqualTo("state-4711"), "the relying party matches the answer to its request by state");
    }

    [Test]
    public async Task YouAuth060_TheRelyingPartysOwnQueryIsKept()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest(Domain, $"{DomainCallback}?session=abc");
        request.PublicKey = "";

        var location = AssertRedirectsTo(await AuthorizeAsync(Host, owner, request), DomainCallback);
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

        var location = AssertRedirectsTo(await AuthorizeAsync(Host, owner, request), AppCallback);
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorAppRevoked),
            "a registered app is a trusted redirect target, and revoked is a reason the app can act on");
    }

    [Test]
    public async Task YouAuth030_ARedirectHostThatIsNotTheDomainClientIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await AuthorizeAsync(Host, owner, DomainRequest(Domain, "https://somewhere-else.org/callback"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "nobody has vouched for that host, so it must not be redirected to, not even with an error");
    }

    [Test]
    public async Task YouAuth030_AnUnregisteredAppWithABadRequestIsRefusedAtTheIdentity()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await AuthorizeAsync(Host, owner, AppRequest(Guid.NewGuid(), "this is not json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "an app nobody has registered has no redirect target the identity can trust");
    }

    private static YouAuthAuthorizeRequest AppRequest(Guid appId, string permissionRequest) => new()
    {
        ClientId = appId.ToString(),
        ClientType = ClientType.app,
        PermissionRequest = permissionRequest,
        PublicKey = NewPublicKey(),
        State = "s",
        RedirectUri = AppCallback
    };
}
