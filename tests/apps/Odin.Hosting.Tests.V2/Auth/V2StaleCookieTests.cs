#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.YouAuth;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests.V2.Auth;

/// <summary>
/// A browser sends whatever cookie it still holds from an old session, to every route. On a public route that makes the
/// caller an anonymous visitor, not a 401, or public images stop loading until the cookie is cleared. Routes that want a
/// signed-in caller still refuse it, and a bearer token, which was sent on purpose, still fails.
/// </summary>
[TestFixture]
public class V2StaleCookieTests : V2Fixture
{
    private const string PublicRoute = UnifiedApiRouteConstants.Auth + "/context";
    private const string SignedInRoute = UnifiedApiRouteConstants.Auth + "/verify-token";

    private string Frodo => PrimaryIdentity;

    private static string Unknown(ClientTokenType type) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32).Append((byte)type).ToArray());

    private async Task<HttpResponseMessage> GetAsync(string route, string? bearer = null, params (string name, string value)[] cookies)
    {
        var request = SignInHttp.Request(HttpMethod.Get, Frodo, route, cookies);
        if (bearer != null)
        {
            request.Headers.Add("Authorization", $"Bearer {bearer}");
        }

        using var client = Host.CreateClient();
        return await client.SendAsync(request);
    }

    private static (string name, string value)[][] StaleCookies() =>
    [
        [(SignInHttp.OwnerCookie, Unknown(ClientTokenType.Owner))],
        [(SignInHttp.AppCookie, Unknown(ClientTokenType.App))],
        [(SignInHttp.HomeCookie, Unknown(ClientTokenType.YouAuth))],
        [(SignInHttp.HomeCookie, Unknown(ClientTokenType.BuiltInBrowserApp))],
    ];

    [Test]
    public async Task AStaleCookie_IsAnonymousOnAPublicRoute_AndRefusedOnASignedInOne()
    {
        var anonymous = await (await GetAsync(PublicRoute)).Content.ReadAsStringAsync();

        foreach (var cookies in StaleCookies())
        {
            var open = await GetAsync(PublicRoute, null, cookies);
            Assert.That(open.StatusCode, Is.EqualTo(HttpStatusCode.OK), cookies[0].name);
            Assert.That(await open.Content.ReadAsStringAsync(), Is.EqualTo(anonymous), cookies[0].name);

            var signedIn = await GetAsync(SignedInRoute, null, cookies);
            Assert.That(signedIn.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), cookies[0].name);
        }
    }

    [Test]
    public async Task ALoggedOutOwnersCookie_IsAnonymousOnAPublicRoute_AndGetsA401OnASignedInOne()
    {
        var owner = await LoginAsOwner(Frodo);
        var cookie = (SignInHttp.OwnerCookie, owner.Token.ToString());
        Assert.That((await GetAsync(SignedInRoute, null, cookie)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logout = await GetAsync($"{SignInHttp.V2OwnerAuth}/logout", null, cookie);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await GetAsync(PublicRoute, null, cookie)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await GetAsync(SignedInRoute, null, cookie)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AStaleBearerToken_StillFailsOnAPublicRoute()
    {
        var response = await GetAsync(PublicRoute, Unknown(ClientTokenType.App));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AValidCookie_IsStillTheOwnerOnAPublicRoute()
    {
        var owner = await LoginAsOwner(Frodo);
        var anonymous = await (await GetAsync(PublicRoute)).Content.ReadAsStringAsync();

        var response = await GetAsync(PublicRoute, null, (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Not.EqualTo(anonymous));
    }
}
