#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>Raw HTTP helpers shared by the two fixtures below: what a browser sends, Cookie header and all.</summary>
internal static class SignInHttp
{
    public const string V1Authorize = OwnerApiPathConstants.YouAuthV1Authorize;
    public const string V2Authorize = UnifiedApiRouteConstants.YouAuthAuthorize;
    public const string V1Token = OwnerApiPathConstants.YouAuthV1Token;
    public const string V2Token = UnifiedApiRouteConstants.YouAuthToken;
    public const string V1OwnerAuth = OwnerApiPathConstants.AuthV1;
    public const string V2OwnerAuth = UnifiedApiRouteConstants.OwnerAuth;
    public const string V1HomeAuth = HomeApiPathConstants.AuthV1;
    public const string V2HomeAuth = UnifiedApiRouteConstants.HomeAuth;
    public const string V1Cacheable = HomeApiPathConstants.CacheableV1;
    public const string V2Cacheable = UnifiedApiRouteConstants.HomeCacheable;
    public const string V1Ident = GuestApiPathConstantsV1.IdentV1;
    public const string V2Ident = UnifiedApiRouteConstants.Ident;

    public const string OwnerCookie = OwnerAuthConstants.CookieName;
    public const string HomeCookie = YouAuthDefaults.XTokenCookieName;
    public const string AppCookie = YouAuthConstants.AppCookieName;

    public static HttpRequestMessage Request(HttpMethod method, string identity, string pathAndQuery, params (string name, string value)[] cookies)
    {
        var request = new HttpRequestMessage(method, $"https://{identity}{pathAndQuery}");
        if (cookies.Length > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => new Cookie(c.name, c.value).ToString())));
        }

        return request;
    }

    public static string AuthorizePath(string route, YouAuthAuthorizeRequest payload) => $"{route}?{payload.ToQueryString()}";

    /// <summary>The attributes a Set-Cookie line gives its cookie, lower-cased; the value is left out.</summary>
    public static string[] CookieAttributes(HttpResponseMessage response, string cookieName)
    {
        var line = SetCookieLine(response, cookieName);
        Assert.That(line, Is.Not.Null, $"no Set-Cookie for {cookieName}");
        return line!.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).Where(a => !a.StartsWith("expires=")).OrderBy(a => a).ToArray();
    }

    public static string? SetCookieLine(HttpResponseMessage response, string cookieName) =>
        response.Headers.TryGetValues("Set-Cookie", out var lines)
            ? lines.FirstOrDefault(l => l.StartsWith($"{cookieName}="))
            : null;

    public static bool IsEncrypted(HttpResponseMessage response) => response.Headers.Contains("X-SSE");

    public static string LocationOf(HttpResponseMessage response) => response.GetHeaderValue("Location") ?? "";

    public static string QueryValue(string url, string name) => YouAuthTestHelper.ParseQueryString(url)[name];
}

/// <summary>
/// The sign-in routes under <c>/api/v2</c>. They are V1's controllers at another path, so most of these run the same
/// case against both and pin that nothing tells them apart; a browser can't see a difference a test here doesn't name.
/// See docs/youauth-unified-authorization.md, "The V2 routes".
/// </summary>
/// <remarks>
/// Driven over raw HTTP with a Cookie header, because the cookies, the Location header and the encryption of the
/// response are the subject. A failed login is the behaviour under some of these tests, but it logs no error.
/// </remarks>
[TestFixture]
public class V2SignInRoutesTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string Domain = "amazoom.org";
    private const string DomainCallback = $"https://{Domain}/callback";

    private static string Frodo => Identities.Frodo;

    private HttpClient Browser() => Host.CreateClient();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = Browser();
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetAsync(string identity, string pathAndQuery, params (string name, string value)[] cookies) =>
        SendAsync(SignInHttp.Request(HttpMethod.Get, identity, pathAndQuery, cookies));

    private Task<HttpResponseMessage> PostJsonAsync(string identity, string path, string json, params (string name, string value)[] cookies)
    {
        var request = SignInHttp.Request(HttpMethod.Post, identity, path, cookies);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendAsync(request);
    }

    // ---------------------------------------------------------------------------------------
    // Authorize: who gets sent to login
    // ---------------------------------------------------------------------------------------

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth040_NoCookieAtAllGoesToLoginAndBackHere(string route)
    {
        await LoginAsOwner(Frodo);
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback));

        var response = await GetAsync(Frodo, path);

        AssertSentToLoginAndBackTo(response, route, path);
    }

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth040_AnOwnerCookieThatIsNotATokenGoesToLogin(string route)
    {
        await LoginAsOwner(Frodo);
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback));

        var response = await GetAsync(Frodo, path, (SignInHttp.OwnerCookie, "garbage"));

        AssertSentToLoginAndBackTo(response, route, path);
    }

    /// <summary>
    /// A valid token that isn't the owner's is still no owner session. This is the case a route on the unified scheme
    /// gets wrong: the token authenticates, the policy forbids, and the browser is left on a 403.
    /// </summary>
    [TestCase(SignInHttp.V1Authorize, SignInHttp.AppCookie)]
    [TestCase(SignInHttp.V2Authorize, SignInHttp.AppCookie)]
    [TestCase(SignInHttp.V1Authorize, SignInHttp.HomeCookie)]
    [TestCase(SignInHttp.V2Authorize, SignInHttp.HomeCookie)]
    public async Task YouAuth040_AValidTokenThatIsNotTheOwnersGoesToLogin(string route, string cookie)
    {
        var owner = await LoginAsOwner(Frodo);
        var (appToken, _) = await owner.Admin.RegisterAppClient(await owner.Admin.RegisterBareApp());
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback));

        var response = await GetAsync(Frodo, path, (cookie, appToken.ToString()));

        AssertSentToLoginAndBackTo(response, route, path);
    }

    [TestCase(SignInHttp.V1Authorize, SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2Authorize, SignInHttp.V2OwnerAuth)]
    public async Task YouAuth040_ASessionThatLoggedOutGoesToLogin(string route, string ownerAuth)
    {
        var token = await LoginInBrowserAsync(ownerAuth);
        var logout = await GetAsync(Frodo, $"{ownerAuth}/logout", (SignInHttp.OwnerCookie, token));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback));

        var response = await GetAsync(Frodo, path, (SignInHttp.OwnerCookie, token));

        AssertSentToLoginAndBackTo(response, route, path);
    }

    private static void AssertSentToLoginAndBackTo(HttpResponseMessage response, string route, string pathAndQuery)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a browser was navigated here; only a redirect reaches a person");
        var location = new Uri(SignInHttp.LocationOf(response));
        Assert.That(location.Host, Is.EqualTo(Frodo));
        Assert.That(location.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Login));

        // Same-origin and absolute, which is what the login page's isSafeReturnUrl lets through
        var returnUrl = new Uri(SignInHttp.QueryValue(location.ToString(), "returnUrl"));
        Assert.That(returnUrl.Host, Is.EqualTo(Frodo));
        Assert.That(returnUrl.AbsolutePath, Is.EqualTo(route));
        Assert.That(returnUrl.PathAndQuery, Is.EqualTo(new Uri($"https://{Frodo}{pathAndQuery}").PathAndQuery));
    }

    // ---------------------------------------------------------------------------------------
    // Authorize, consent, token: the whole flow on one route
    // ---------------------------------------------------------------------------------------

    [TestCase(SignInHttp.V1Authorize, SignInHttp.V1Token)]
    [TestCase(SignInHttp.V2Authorize, SignInHttp.V2Token)]
    [TestCase(SignInHttp.V2Authorize, SignInHttp.V1Token)]
    public async Task YouAuth_ADomainSignsInFromAuthorizeToToken(string route, string tokenRoute)
    {
        var owner = await LoginAsOwner(Frodo);
        var ownerCookie = (SignInHttp.OwnerCookie, owner.Token.ToString());
        var keyPair = NewKeyPair();
        var request = DomainRequest(Domain, DomainCallback, keyPair);
        request.Cipher = YouAuthDefaults.CipherAesGcm;

        // [050] to the consent page, carrying this route as where to come back to
        var first = await GetAsync(Frodo, SignInHttp.AuthorizePath(route, request), ownerCookie);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var consentPage = SignInHttp.LocationOf(first);
        Assert.That(new Uri(consentPage).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));
        var returnUrl = SignInHttp.QueryValue(consentPage, "returnUrl");
        Assert.That(new Uri(returnUrl).AbsolutePath, Is.EqualTo(route), "the consent page goes back where the sign-in started");

        // [055] the consent form posts to that route, and is redirected back to it
        var consent = await PostConsentAsync(route, returnUrl, ownerCookie);
        Assert.That(consent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(SignInHttp.LocationOf(consent), Is.EqualTo(returnUrl));

        // [080] on to the relying party
        var second = await GetAsync(Frodo, new Uri(returnUrl).PathAndQuery, ownerCookie);
        Assert.That(SignInHttp.IsEncrypted(second), Is.False);
        var callback = SignInHttp.LocationOf(second);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), callback);
        Assert.That(callback, Does.StartWith(DomainCallback));

        // [100] to [140], the token sealed with the cipher asked for
        var query = YouAuthTestHelper.ParseQueryString(callback);
        Assert.That(query[YouAuthDefaults.Identity], Is.EqualTo(Frodo));
        var secret = keyPair.ExchangeSecretWith(query[YouAuthDefaults.PublicKey], query[YouAuthDefaults.Salt]);
        var digest = SHA256.HashData(secret.GetKey()).ToBase64();
        var tokenResponse = await PostJsonAsync(Frodo, tokenRoute, OdinSystemSerializer.Serialize(new YouAuthTokenRequest { SecretDigest = digest }));
        var body = await tokenResponse.Content.ReadAsStringAsync();
        Assert.That(tokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

        var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(body)!;
        Assert.That(token.Cipher, Is.EqualTo(YouAuthDefaults.CipherAesGcm));
        var catBytes = AesGcm.Decrypt(token.Base64ClientAuthTokenCipher!.FromBase64(), secret, token.Base64ClientAuthTokenIv!.FromBase64());
        Assert.That(ClientAuthenticationToken.FromPortableBytes(catBytes).ClientTokenType, Is.EqualTo(ClientTokenType.YouAuth));
    }

    /// <summary>
    /// The consent handler refuses a return URL with any path but its own. So the consent page can't post to one route
    /// for every sign-in: a V1 sign-in, which is every third-party app and chat-kmp today, has to post to V1.
    /// </summary>
    [TestCase(SignInHttp.V2Authorize, SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V1Authorize, SignInHttp.V2Authorize)]
    public async Task YouAuth055_ConsentRefusesAReturnUrlOfTheOtherRoute(string postedTo, string returnRoute)
    {
        var owner = await LoginAsOwner(Frodo);
        var returnUrl = $"https://{Frodo}{SignInHttp.AuthorizePath(returnRoute, DomainRequest(Domain, DomainCallback))}";

        var response = await PostConsentAsync(postedTo, returnUrl, (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task YouAuth055_ConsentWithNoSessionGoesToLoginAndStoresNothing()
    {
        var owner = await LoginAsOwner(Frodo);
        var request = DomainRequest(Domain, DomainCallback);
        var returnUrl = $"https://{Frodo}{SignInHttp.AuthorizePath(SignInHttp.V2Authorize, request)}";

        var response = await PostConsentAsync(SignInHttp.V2Authorize, returnUrl);
        Assert.That(new Uri(SignInHttp.LocationOf(response)).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Login));

        var authorize = await GetAsync(Frodo, new Uri(returnUrl).PathAndQuery, (SignInHttp.OwnerCookie, owner.Token.ToString()));
        Assert.That(new Uri(SignInHttp.LocationOf(authorize)).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));
    }

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth045_AnUnregisteredAppGoesToRegistrationAndBackHere(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var appId = Guid.NewGuid();
        var request = new YouAuthAuthorizeRequest
        {
            ClientId = appId.ToString(),
            ClientType = ClientType.app,
            PermissionRequest = OdinSystemSerializer.Serialize(new YouAuthAppParameters
            {
                AppId = appId.ToString(),
                AppName = "A New App",
                AppSlug = "a-new-app",
                AppOrigin = "app.example.org",
                ClientFriendly = "Firefox | macOS",
                DrivesParam = "[]",
                Return = "backend-will-decide",
            }),
            PublicKey = NewPublicKey(),
            State = "s",
            RedirectUri = "https://app.example.org/callback"
        };

        var response = await GetAsync(Frodo, SignInHttp.AuthorizePath(route, request), (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var location = new Uri(SignInHttp.LocationOf(response));
        Assert.That(location.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.AppReg));
        var parameters = YouAuthAppParameters.FromQueryString(location.Query);
        Assert.That(new Uri(parameters.Return).AbsolutePath, Is.EqualTo(route), "the registration page's way back");
        Assert.That(parameters.Cancel, Is.EqualTo(request.RedirectUri));
    }

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth030_ARedirectHostThatIsNotTheDomainClientIsRefusedHere(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Domain, "https://somewhere-else.org/callback"));

        var response = await GetAsync(Frodo, path, (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "not an open redirector");
    }

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth030_YouAuthToYourselfIsRefused(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var path = SignInHttp.AuthorizePath(route, DomainRequest(Frodo, $"https://{Frodo}/callback"));

        var response = await GetAsync(Frodo, path, (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public async Task YouAuth060_ABadRequestToATrustedTargetGoesBackWithAnError(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var request = DomainRequest(Domain, $"{DomainCallback}?session=abc");
        request.PublicKey = "";
        request.State = "state-4711";

        var response = await GetAsync(Frodo, SignInHttp.AuthorizePath(route, request), (SignInHttp.OwnerCookie, owner.Token.ToString()));

        var query = AssertRedirectsTo(response, DomainCallback);
        Assert.That(query[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
        Assert.That(query[YouAuthDefaults.State], Is.EqualTo("state-4711"));
        Assert.That(query["session"], Is.EqualTo("abc"));
    }

    [TestCase(SignInHttp.V1Token)]
    [TestCase(SignInHttp.V2Token)]
    public async Task YouAuth120_AnUnknownDigestIs404WithNoLogin(string route)
    {
        await LoginAsOwner(Frodo);
        var digest = SHA256.HashData(Guid.NewGuid().ToByteArray()).ToBase64();

        var response = await PostJsonAsync(Frodo, route, OdinSystemSerializer.Serialize(new YouAuthTokenRequest { SecretDigest = digest }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private Task<HttpResponseMessage> PostConsentAsync(string route, string returnUrl, params (string name, string value)[] cookies)
    {
        var request = SignInHttp.Request(HttpMethod.Post, Frodo, route, cookies);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl },
            { YouAuthAuthorizeConsentGiven.ConsentRequirementName, "" },
        });
        return SendAsync(request);
    }

    // ---------------------------------------------------------------------------------------
    // Ident
    // ---------------------------------------------------------------------------------------

    [TestCase(SignInHttp.V1Ident)]
    [TestCase(SignInHttp.V2Ident)]
    public async Task IdentNamesThisIdentityToAnyoneAndAnyOrigin(string route)
    {
        await LoginAsOwner(Frodo);
        var request = SignInHttp.Request(HttpMethod.Get, Frodo, route);
        request.Headers.Add("Origin", "https://somewhere-else.org");

        var response = await SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = OdinSystemSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync())!;
        Assert.That(body["odinId"].ToString(), Is.EqualTo(Frodo));
        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.True);
    }

    // ---------------------------------------------------------------------------------------
    // Owner login
    // ---------------------------------------------------------------------------------------

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task OwnerLoginGivesTheCookiesAndAPlainSharedSecret(string route)
    {
        var response = await RawLoginAsync(route, OwnerLogin.DefaultPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = OdinSystemSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync())!;
        Assert.That(result.Keys, Does.Contain("sharedSecret"), "the shared secret is not itself sealed with a shared secret");

        var expected = new[] { "httponly", "path=/", "samesite=strict", "secure" };
        Assert.That(SignInHttp.CookieAttributes(response, SignInHttp.OwnerCookie), Is.EqualTo(expected));
        Assert.That(SignInHttp.CookieAttributes(response, "dpnc"), Is.EqualTo(expected));
    }

    /// <summary>What a cookie jar does with the cookie: it travels between the two API generations in both directions.</summary>
    [TestCase(SignInHttp.V1OwnerAuth, "/api/v2/auth/verify-token")]
    [TestCase(SignInHttp.V2OwnerAuth, "/api/owner/v1/config/system/isconfigured")]
    public async Task TheOwnerCookieIsSentToTheOtherApiGeneration(string loggedInAt, string otherPath)
    {
        var response = await RawLoginAsync(loggedInAt, OwnerLogin.DefaultPassword);

        var jar = new CookieContainer();
        var setAt = new Uri($"https://{Frodo}{loggedInAt}");
        jar.SetCookies(setAt, SignInHttp.SetCookieLine(response, SignInHttp.OwnerCookie)!);

        Assert.That(jar.GetCookieHeader(new Uri($"https://{Frodo}{otherPath}")), Does.StartWith($"{SignInHttp.OwnerCookie}="));
        Assert.That(jar.GetCookieHeader(new Uri($"https://{Identities.Sam}{otherPath}")), Is.Empty, "and to this host only");
    }

    [Test]
    public async Task ASessionFromEitherLoginWorksOnBothVerifyRoutesAndOnV2()
    {
        foreach (var loginRoute in new[] { SignInHttp.V1OwnerAuth, SignInHttp.V2OwnerAuth })
        {
            var token = await LoginInBrowserAsync(loginRoute);

            foreach (var verifyRoute in new[] { SignInHttp.V1OwnerAuth, SignInHttp.V2OwnerAuth })
            {
                Assert.That(await VerifyTokenAsync(verifyRoute, (SignInHttp.OwnerCookie, token)), Is.True, $"{loginRoute} then {verifyRoute}");
            }

            var v2 = await GetAsync(Frodo, $"{UnifiedApiRouteConstants.Auth}/verify-token", (SignInHttp.OwnerCookie, token));
            Assert.That(v2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task AWrongPasswordIsRefusedAndSetsNoCookie(string route)
    {
        await LoginAsOwner(Frodo);

        var response = await RawLoginAsync(route, "not the password");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(SignInHttp.SetCookieLine(response, SignInHttp.OwnerCookie), Is.Null);
    }

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task ANonceIsAnsweredOnceForOneLogin(string route)
    {
        await LoginAsOwner(Frodo);

        // Two logins on one nonce is what a cached answer to this route would mean
        using var client = RoutedAuthClient(route);
        var svc = RestService.For<IOwnerAuthenticationClient>(client);
        var eccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var reply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(client, OwnerLogin.DefaultPassword, eccKey);
        Assert.That((await svc.Authenticate(reply)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await svc.Authenticate(reply)).StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), "a nonce is used once");

        var nonce = await GetAsync(Frodo, $"{route}/nonce");
        Assert.That(nonce.Headers.CacheControl?.NoStore, Is.True, "never cached, or two parallel logins get one nonce");
    }

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task VerifyTokenSaysFalseWithoutAValidSessionAndNeverRefuses(string route)
    {
        await LoginAsOwner(Frodo);

        Assert.That(await VerifyTokenAsync(route), Is.False, "no cookie");
        Assert.That(await VerifyTokenAsync(route, (SignInHttp.OwnerCookie, "garbage")), Is.False, "not a token");
        Assert.That(await VerifyTokenAsync(route, (SignInHttp.OwnerCookie, new ClientAuthenticationToken
        {
            Id = Guid.NewGuid(),
            AccessTokenHalfKey = Guid.NewGuid().ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.Owner
        }.ToString())), Is.False, "a token nobody issued");
    }

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task OwnerLogoutEndsTheSessionAndDeletesTheCookieAsItWasSet(string route)
    {
        var token = await LoginInBrowserAsync(route);
        var cookie = (SignInHttp.OwnerCookie, token);
        Assert.That(await VerifyTokenAsync(route, cookie), Is.True);

        var logout = await GetAsync(Frodo, $"{route}/logout", cookie);

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await logout.Content.ReadAsStringAsync(), Is.EqualTo("true"));
        Assert.That(SignInHttp.CookieAttributes(logout, SignInHttp.OwnerCookie),
            Is.EqualTo(new[] { "httponly", "path=/", "samesite=strict", "secure" }), "a delete a browser honours for a Secure, Strict cookie");
        Assert.That(SignInHttp.SetCookieLine(logout, SignInHttp.OwnerCookie), Does.Contain("expires=Thu, 01 Jan 1970"));

        Assert.That(await VerifyTokenAsync(route, cookie), Is.False, "the server side is gone too");
        var v2 = await GetAsync(Frodo, $"{UnifiedApiRouteConstants.Auth}/verify-token", cookie);
        Assert.That(v2.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task OwnerLogoutWithNoCookieStillAnswers()
    {
        await LoginAsOwner(Frodo);

        var logout = await GetAsync(Frodo, $"{SignInHttp.V2OwnerAuth}/logout");

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// These answer as plain JSON for a caller with a valid session too, who would otherwise get an encrypted body the
    /// login page, holding no shared secret, can't open. V1 lists the path; V2 says so on the controller.
    /// </summary>
    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public async Task OwnerAuthRoutesAreNeverEncrypted(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var cookie = (SignInHttp.OwnerCookie, owner.Token.ToString());

        foreach (var path in new[] { "nonce", "getsalts", "verifyToken", "publickey_ecc?keyType=offlineKey" })
        {
            var response = await GetAsync(Frodo, $"{route}/{path}", cookie);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            Assert.That(SignInHttp.IsEncrypted(response), Is.False, path);
        }
    }

    /// <summary>The rest of the controller answers each request on V2 exactly as on V1, with no session.</summary>
    [TestCase("GET", "getsalts", "")]
    [TestCase("GET", "publickey_ecc?keyType=offlineKey", "")]
    [TestCase("GET", "zk-phrase-recovery", "")]
    [TestCase("POST", "ispasswordset", "")]
    [TestCase("POST", "passwd", "{}")]
    [TestCase("POST", "zk-reset", "{}")]
    public async Task TheOtherPasswordRoutesAnswerAsOnV1(string method, string path, string body)
    {
        await LoginAsOwner(Frodo);

        async Task<HttpResponseMessage> Call(string route)
        {
            var request = SignInHttp.Request(new HttpMethod(method), Frodo, $"{route}/{path}");
            if (method == "POST")
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return await SendAsync(request);
        }

        var v1 = await Call(SignInHttp.V1OwnerAuth);
        var v2 = await Call(SignInHttp.V2OwnerAuth);

        Assert.That(v2.StatusCode, Is.EqualTo(v1.StatusCode));
        Assert.That(SignInHttp.IsEncrypted(v2), Is.False);
    }

    /// <summary>The login page's first question, on a brand-new identity with no password: Sam's, before anyone logs in.</summary>
    [Test]
    public async Task IsPasswordSetAnswersOnEitherRoute()
    {
        await LoginAsOwner(Frodo);

        foreach (var route in new[] { SignInHttp.V1OwnerAuth, SignInHttp.V2OwnerAuth })
        {
            var response = await PostJsonAsync(Frodo, $"{route}/ispasswordset", "");
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("true"), route);
        }
    }

    /// <summary>
    /// Why authorize isn't on the unified scheme: with no token that scheme answers an anonymous success, so a policy
    /// that wants an owner forbids instead of challenging, and its challenge, the redirect to login, never runs.
    /// </summary>
    [Test]
    public async Task TheUnifiedSchemeForbidsAnAnonymousCallerRatherThanChallenging()
    {
        await LoginAsOwner(Frodo);

        var response = await GetAsync(Frodo, $"{UnifiedApiRouteConstants.Onboarding}/invited-by");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // ---------------------------------------------------------------------------------------
    // The home site
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The public page is asking about its visitor's XT32 cookie. An owner's cookie, good or stale, is not an answer:
    /// the owner console has its own check. The unified scheme would say true for one and 401 for the other.
    /// </summary>
    [TestCase(SignInHttp.V1HomeAuth)]
    [TestCase(SignInHttp.V2HomeAuth)]
    public async Task IsAuthenticatedIsAboutTheVisitorCookieOnly(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var path = $"{route}/{HomeApiPathConstants.IsAuthenticatedMethodName}";

        Assert.That(await BodyOfAsync(path), Is.EqualTo("false"), "no cookie");
        Assert.That(await BodyOfAsync(path, (SignInHttp.OwnerCookie, owner.Token.ToString())), Is.EqualTo("false"), "the owner's");
        Assert.That(await BodyOfAsync(path, (SignInHttp.OwnerCookie, "garbage")), Is.EqualTo("false"), "a stale owner's");
        Assert.That(await BodyOfAsync(path, (SignInHttp.HomeCookie, "garbage")), Is.EqualTo("false"), "not a token");
    }

    /// <summary>
    /// The whole home-site sign-in with the callback on this route, as the public app names it in redirect_uri: Sam's
    /// identity accepts it, Frodo sets the cookie, and the cookie then signs the visitor in on both routes.
    /// </summary>
    [TestCase(SignInHttp.V1HomeAuth)]
    [TestCase(SignInHttp.V2HomeAuth)]
    public async Task AVisitorSignsInOnTheHomeSiteAndOut(string route)
    {
        var response = await SignInOnFrodosHomeSiteAsync(route);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var location = SignInHttp.LocationOf(response);
        Assert.That(location, Does.StartWith(FrodosPage).And.Contain("?r=").And.Contain("&ecc="));
        Assert.That(location, Does.Not.Contain("error="));

        Assert.That(SignInHttp.CookieAttributes(response, SignInHttp.HomeCookie), Is.EqualTo(new[] { "httponly", "path=/", "samesite=strict", "secure" }));

        var xt32 = response.GetCookies()[SignInHttp.HomeCookie];
        var cookie = (SignInHttp.HomeCookie, HttpUtility.UrlDecode(xt32));
        foreach (var other in new[] { SignInHttp.V1HomeAuth, SignInHttp.V2HomeAuth })
        {
            var isAuthenticated = await GetAsync(Frodo, $"{other}/{HomeApiPathConstants.IsAuthenticatedMethodName}", cookie);
            Assert.That(await isAuthenticated.Content.ReadAsStringAsync(), Is.EqualTo("true"), other);
            Assert.That(SignInHttp.IsEncrypted(isAuthenticated), Is.False, other);
        }

        var delete = await GetAsync(Frodo, $"{route}/{HomeApiPathConstants.DeleteTokenMethodName}", cookie);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(SignInHttp.CookieAttributes(delete, SignInHttp.HomeCookie), Does.Contain("path=/"));
        Assert.That(SignInHttp.SetCookieLine(delete, SignInHttp.HomeCookie), Does.Contain("expires=Thu, 01 Jan 1970"));
    }

    [TestCase(SignInHttp.V1HomeAuth)]
    [TestCase(SignInHttp.V2HomeAuth)]
    public async Task TheCallbackSendsAFailedSignInBackToThePage(string route)
    {
        await LoginAsOwner(Frodo);
        await LoginAsOwner(Identities.Sam);
        var state = HttpUtility.UrlEncode(OdinSystemSerializer.Serialize(new HomeAuthenticationState
        {
            FinalUrl = FrodosPage,
            ReturnUrl = "/",
            EccPk64 = NewKeyPair().PublicKeyJwk,
        }));
        var publicKey = HttpUtility.UrlEncode(NewKeyPair().PublicKeyJwk);
        var salt = HttpUtility.UrlEncode(Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        // Sam's identity has no token for this digest
        var response = await GetAsync(Frodo,
            $"{route}/{HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName}?identity={Identities.Sam}&public_key={publicKey}&salt={salt}&state={state}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(SignInHttp.LocationOf(response), Is.EqualTo($"{FrodosPage}?error=remoteValidationCallFailed"));
        Assert.That(SignInHttp.SetCookieLine(response, SignInHttp.HomeCookie), Is.Null);
    }

    private Task<HttpResponseMessage> SignInOnFrodosHomeSiteAsync(string callbackRoute) =>
        SignInOnFrodosHomeSiteCoreAsync($"https://{Frodo}{callbackRoute}/{HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName}");

    private static string FrodosPage => $"https://{Frodo}/authorization-code-callback";

    private async Task<HttpResponseMessage> SignInOnFrodosHomeSiteCoreAsync(string callback)
    {
        await LoginAsOwner(Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var offlineKey = await Host.AnonymousRefitFor<IPublicPrivateKeyHttpClientForOwner>(Frodo).GetEccOfflinePublicKey();
        var request = DomainRequest(Frodo, callback);
        request.PublicKey = offlineKey.Content!;
        request.State = OdinSystemSerializer.Serialize(new HomeAuthenticationState
        {
            FinalUrl = FrodosPage,
            ReturnUrl = "/",
            EccPk64 = NewKeyPair().PublicKeyJwk,
            Cipher = YouAuthDefaults.CipherAesGcm
        });

        // Sam's identity sends the browser to Frodo's callback, on whichever route the page named; the test plays the browser.
        var callbackUrl = await AuthorizeWithConsentAsync(Host, sam, request);
        Assert.That(new Uri(callbackUrl).AbsolutePath, Is.EqualTo(new Uri(callback).AbsolutePath));
        using var browser = Browser();
        return await browser.GetAsync(callbackUrl);
    }

    private async Task<string> BodyOfAsync(string path, params (string name, string value)[] cookies)
    {
        var response = await GetAsync(Frodo, path, cookies);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
        return await response.Content.ReadAsStringAsync();
    }

    // ---------------------------------------------------------------------------------------
    // The home page's cache
    // ---------------------------------------------------------------------------------------

    [TestCase(SignInHttp.V1Cacheable)]
    [TestCase(SignInHttp.V2Cacheable)]
    public async Task TheCachedHomeQueryAnswersAnonymouslyAndSaysHowLongToCache(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "public", allowAnonymousReads: true);
        await UploadAsync(owner, drive, AccessControlList.Anonymous);

        var response = await QueryHomeCacheAsync(route, drive, Guid.NewGuid().ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(SignInHttp.IsEncrypted(response), Is.False);
        Assert.That(ResultCount(body), Is.EqualTo(1));
    }

    /// <summary>
    /// The cache keeps one answer per tenant and query name, for every caller. An owner's cookie must not make the query
    /// run as the owner: what it found would then be served to every anonymous visitor.
    /// </summary>
    [TestCase(SignInHttp.V1Cacheable)]
    [TestCase(SignInHttp.V2Cacheable)]
    public async Task TheOwnerCookieDoesNotFillTheHomeCacheWithWhatOnlyTheOwnerSees(string route)
    {
        var owner = await LoginAsOwner(Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "public drive, private file", allowAnonymousReads: true);
        await UploadAsync(owner, drive, AccessControlList.OwnerOnly);
        var name = Guid.NewGuid().ToString();

        var asOwner = await QueryHomeCacheAsync(route, drive, name, (SignInHttp.OwnerCookie, owner.Token.ToString()));

        var body = await asOwner.Content.ReadAsStringAsync();
        Assert.That(asOwner.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(SignInHttp.IsEncrypted(asOwner), Is.False, "the owner's cookie was read; a query run as the owner is encrypted for the owner");
        Assert.That(ResultCount(body), Is.EqualTo(0), "the owner's cookie must not count here");

        var asAnonymous = await QueryHomeCacheAsync(route, drive, name);
        Assert.That(ResultCount(await asAnonymous.Content.ReadAsStringAsync()), Is.EqualTo(0));
    }

    private static int ResultCount(string body)
    {
        var response = OdinSystemSerializer.Deserialize<QueryBatchCollectionResponse>(body)!;
        return response.Results.Single().SearchResults.Count();
    }

    private Task<HttpResponseMessage> QueryHomeCacheAsync(string route, TargetDrive drive, string name, params (string name, string value)[] cookies)
    {
        var body = OdinSystemSerializer.Serialize(new QueryBatchCollectionRequest
        {
            Queries =
            [
                new CollectionQueryParamSection
                {
                    Name = name,
                    QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
                }
            ]
        });
        var request = SignInHttp.Request(HttpMethod.Post, Frodo, $"{route}/qbc", cookies);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add(OdinHeaderNames.FileSystemTypeHeader, nameof(FileSystemType.Standard));
        return SendAsync(request);
    }

    private static async Task UploadAsync(OwnerSession owner, TargetDrive drive, AccessControlList acl)
    {
        var metadata = SampleMetadataData.Create(fileType: 101, acl: acl);
        var upload = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TheV2SwaggerDocumentStillBuilds()
    {
        await LoginAsOwner(Frodo);
        using var client = Host.CreateAnonymousClient(Frodo);
        var doc = await client.GetAsync("/swagger/v2/swagger.json");
        var body = await doc.Content.ReadAsStringAsync();
        Assert.That(doc.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain(SignInHttp.V2Authorize).And.Contain($"{SignInHttp.V2OwnerAuth}/nonce"));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>The owner password dance, with its Refit client's V1 paths sent to <paramref name="route"/>.</summary>
    private HttpClient RoutedAuthClient(string route) =>
        new(new RerouteHandler(route, Host.Server.CreateHandler())) { BaseAddress = new Uri($"https://{Frodo}/") };

    private async Task<HttpResponseMessage> RawLoginAsync(string route, string password)
    {
        await LoginAsOwner(Frodo);
        using var client = RoutedAuthClient(route);
        var eccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var reply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(client, password, eccKey);
        var response = await RestService.For<IOwnerAuthenticationClient>(client).Authenticate(reply);
        return response.RequestMessage != null ? ToRaw(response) : throw new InvalidOperationException();
    }

    private static HttpResponseMessage ToRaw(ApiResponse<Odin.Hosting.Controllers.OwnerToken.Auth.OwnerAuthenticationResult> response)
    {
        var raw = new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.IsSuccessStatusCode ? OdinSystemSerializer.Serialize(response.Content!) : "")
        };
        foreach (var header in response.Headers)
        {
            raw.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return raw;
    }

    /// <summary>Logs in at <paramref name="route"/> and returns the owner cookie's value as a browser would hold it.</summary>
    private async Task<string> LoginInBrowserAsync(string route)
    {
        var response = await RawLoginAsync(route, OwnerLogin.DefaultPassword);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return HttpUtility.UrlDecode(response.GetCookies()[SignInHttp.OwnerCookie]);
    }

    private async Task<bool> VerifyTokenAsync(string route, params (string name, string value)[] cookies)
    {
        var response = await GetAsync(Frodo, $"{route}/verifyToken", cookies);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return bool.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class RerouteHandler(string route, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(request.RequestUri!.ToString().Replace(OwnerApiPathConstants.AuthV1, route));
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>
/// A browser adds the owner's cookie to every request to the identity's host, so an app's call can carry both. The bearer
/// token wins whether the CDN is on or off: the call is the app's, and its logout ends the app's session. Off is the
/// default, and the one set here because the test host turns the CDN on.
/// </summary>
[TestFixture]
public class V2AppLogoutWithOwnerCookieTests : V2Fixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?> { ["Cdn:Enabled"] = "false" };

    private string Frodo => PrimaryIdentity;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer = null, params (string name, string value)[] cookies)
    {
        var request = SignInHttp.Request(method, Frodo, path, cookies);
        if (bearer != null)
        {
            request.Headers.Add("Authorization", $"Bearer {bearer}");
        }

        using var client = Host.CreateClient();
        return await client.SendAsync(request);
    }

    private async Task<HttpStatusCode> AppSessionAsync(string appToken) =>
        (await SendAsync(HttpMethod.Get, $"{UnifiedApiRouteConstants.Auth}/verify-token", appToken)).StatusCode;

    private async Task<HttpStatusCode> SendOwnerProbeAsync(string ownerToken) =>
        (await SendAsync(HttpMethod.Get, $"{UnifiedApiRouteConstants.Auth}/verify-token", null, (SignInHttp.OwnerCookie, ownerToken))).StatusCode;

    [Test]
    public async Task WithTheOwnerCookieThereTheBearerTokenStillWins()
    {
        var owner = await LoginAsOwner(Frodo);
        var (appToken, _) = await owner.Admin.RegisterAppClient(await owner.Admin.RegisterBareApp());
        Assert.That(await AppSessionAsync(appToken.ToString()), Is.EqualTo(HttpStatusCode.OK));

        await SendAsync(HttpMethod.Post, $"{UnifiedApiRouteConstants.Auth}/logout", appToken.ToString(), (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(await AppSessionAsync(appToken.ToString()), Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(await SendOwnerProbeAsync(owner.Token.ToString()), Is.EqualTo(HttpStatusCode.OK), "the owner's session must survive");
    }

    [Test]
    public async Task OnceTheOwnerHasLoggedOutTheAppsLogoutEndsTheAppsSession()
    {
        var owner = await LoginAsOwner(Frodo);
        var (appToken, _) = await owner.Admin.RegisterAppClient(await owner.Admin.RegisterBareApp());
        Assert.That(await AppSessionAsync(appToken.ToString()), Is.EqualTo(HttpStatusCode.OK));

        var ownerLogout = await SendAsync(HttpMethod.Get, $"{SignInHttp.V2OwnerAuth}/logout", null, (SignInHttp.OwnerCookie, owner.Token.ToString()));
        Assert.That(ownerLogout.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The browser has dropped the cookie now, so the request carries the bearer token alone
        await SendAsync(HttpMethod.Post, $"{UnifiedApiRouteConstants.Auth}/logout", appToken.ToString());

        Assert.That(await AppSessionAsync(appToken.ToString()), Is.Not.EqualTo(HttpStatusCode.OK));
    }
}

/// <summary>
/// A recovery-key reset with an empty body is a null reference on V1, and so on V2: a 500 and an error in the log. Pinned
/// apart from the other password routes so that toleration covers this test alone.
/// </summary>
[TestFixture]
public class V2SignInMalformedResetTests : V2Fixture
{
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings => ["Object reference not set"];

    [Test]
    public async Task AnEmptyResetAnswersAsOnV1()
    {
        await LoginAsOwner(PrimaryIdentity);

        async Task<HttpStatusCode> PostAsync(string route)
        {
            var request = SignInHttp.Request(HttpMethod.Post, PrimaryIdentity, $"{route}/resetpasswdrk");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var client = Host.CreateClient();
            return (await client.SendAsync(request)).StatusCode;
        }

        Assert.That(await PostAsync(SignInHttp.V2OwnerAuth), Is.EqualTo(await PostAsync(SignInHttp.V1OwnerAuth)));
    }
}

/// <summary>
/// Sign-in while a version upgrade runs: the same doors stay open on V2 as on V1, the same refusals carry the same
/// header, and the owner sent to authorize reaches the upgrade screen with the way back to this route.
/// </summary>
/// <remarks><c>NonParallelizable</c>: the run state is a tenant singleton and this sets it (see <c>VersionUpgradeGuardTests</c>).</remarks>
[TestFixture]
[NonParallelizable]
public class V2SignInDuringUpgradeTests : V2Fixture
{
    private const string Domain = "amazoom.org";
    private const string DomainCallback = $"https://{Domain}/callback";

    private string Tom => Identities.TomBombadil;

    protected override string[] HostIdentities => [Identities.TomBombadil];

    private async Task WhileUpgradingAsync(Func<OwnerSession, Task> test)
    {
        var owner = await LoginAsOwner(Tom);
        var runState = Host.GetTenantScope(Tom).Resolve<VersionUpgradeRunState>();
        try
        {
            runState.SetRunning(true);
            await test(owner);
        }
        finally
        {
            runState.SetRunning(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = Host.CreateClient();
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetAsync(string pathAndQuery, params (string name, string value)[] cookies) =>
        SendAsync(SignInHttp.Request(HttpMethod.Get, Tom, pathAndQuery, cookies));

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public Task TheOwnerAtAuthorizeGoesToTheUpgradeScreenAndBackHere(string route) => WhileUpgradingAsync(async owner =>
    {
        var response = await GetAsync(SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback)), (SignInHttp.OwnerCookie, owner.Token.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var location = SignInHttp.LocationOf(response);
        Assert.That(location, Does.StartWith(OwnerFrontendPathConstants.DataUpgrade));
        var returnUrl = HttpUtility.ParseQueryString(location[location.IndexOf('?')..])["returnUrl"]!;
        Assert.That(new Uri(returnUrl).AbsolutePath, Is.EqualTo(route));
    });

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public Task NoSessionAtAuthorizeGoesToLoginNotTheUpgradeScreen(string route) => WhileUpgradingAsync(async _ =>
    {
        var response = await GetAsync(SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(new Uri(SignInHttp.LocationOf(response)).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Login));
    });

    [TestCase(SignInHttp.V1Authorize)]
    [TestCase(SignInHttp.V2Authorize)]
    public Task TheConsentFormGoesToTheUpgradeScreenAndComesBackToConsent(string route) => WhileUpgradingAsync(async owner =>
    {
        var returnUrl = $"https://{Tom}{SignInHttp.AuthorizePath(route, DomainRequest(Domain, DomainCallback))}";
        var request = SignInHttp.Request(HttpMethod.Post, Tom, route, (SignInHttp.OwnerCookie, owner.Token.ToString()));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl } });

        var response = await SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(SignInHttp.LocationOf(response), Does.StartWith(OwnerFrontendPathConstants.DataUpgrade).And.Contain(WebUtility.UrlEncode(route)));
    });

    /// <summary>A forged form can't turn the upgrade screen into an open redirect; nor can one naming the other route.</summary>
    [TestCase(SignInHttp.V2Authorize, "https://somewhere-else.org/callback")]
    [TestCase(SignInHttp.V2Authorize, "/api/owner/v1/youauth/authorize")]
    public Task ABadConsentReturnUrlStaysRefused(string route, string returnUrl) => WhileUpgradingAsync(async owner =>
    {
        var request = SignInHttp.Request(HttpMethod.Post, Tom, route, (SignInHttp.OwnerCookie, owner.Token.ToString()));
        var target = returnUrl.StartsWith('/') ? $"https://{Tom}{returnUrl}" : returnUrl;
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { { YouAuthAuthorizeConsentGiven.ReturnUrlName, target } });

        var response = await SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Headers.Contains("Location"), Is.False);
    });

    [TestCase(SignInHttp.V1OwnerAuth)]
    [TestCase(SignInHttp.V2OwnerAuth)]
    public Task OwnerAuthAnswersWithNoUpgradeHeader(string route) => WhileUpgradingAsync(async owner =>
    {
        var cookie = (SignInHttp.OwnerCookie, owner.Token.ToString());

        foreach (var path in new[] { "nonce", "getsalts", "verifyToken" })
        {
            var response = await GetAsync($"{route}/{path}", cookie);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            Assert.That(response.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.False, path);
        }
    });

    [TestCase(SignInHttp.V1Ident)]
    [TestCase(SignInHttp.V2Ident)]
    public Task IdentStillSaysThisIsAnIdentity(string route) => WhileUpgradingAsync(async _ =>
    {
        var response = await GetAsync(route);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True);
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(Tom));
    });

    [TestCase("POST", SignInHttp.V2Token)]
    [TestCase("GET", SignInHttp.V2HomeAuth + "/" + HomeApiPathConstants.IsAuthenticatedMethodName)]
    [TestCase("GET", SignInHttp.V2HomeAuth + "/" + HomeApiPathConstants.DeleteTokenMethodName)]
    [TestCase("GET", SignInHttp.V2HomeAuth + "/" + HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName)]
    [TestCase("GET", SignInHttp.V2Cacheable + "/qbc")]
    public Task EverythingElseIsRefusedAndSaysWhy(string method, string path) => WhileUpgradingAsync(async _ =>
    {
        var request = SignInHttp.Request(new HttpMethod(method), Tom, path);
        if (method == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True);
    });
}
