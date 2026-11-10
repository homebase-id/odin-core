#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.YouAuth;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// One identity signing in on another's home site: Sam on Frodo's. Frodo's identity is the relying
/// party (<c>HomeAuthenticationController</c>), and its public page is a second client of its own:
/// the page hands its key and its cipher over in <c>state</c>, and gets the sign-in result back
/// sealed for it at [400]. Both halves negotiate the cipher by declaration, so a page bundle and a
/// peer of any age agree. Step numbers in the names are the flow diagram's.
/// </summary>
/// <remarks>
/// The browser's part is played by hand: Frodo's offline ECC key and callback go on the request to
/// Sam's identity, the consent detour is walked, and the callback Sam's identity redirects to is
/// then fetched on Frodo's. Frodo's token exchange with Sam runs through the in-process peer client.
/// </remarks>
[TestFixture]
public class HomeSiteLoginTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static string FrodosCallback => $"https://{Identities.Frodo}{HomeApiPathConstants.AuthV1}/{HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName}";
    private static string FrodosPage => $"https://{Identities.Frodo}/authorization-code-callback";

    [TestCase("")]
    [TestCase("aes-gcm")]
    public async Task YouAuth150_TheHomeSiteOpensTheTokenWithWhicheverCipherThePeerEchoes(string peerCipher)
    {
        var (response, _) = await SignInOnFrodosHomeSiteAsync(peerCipher, pageCipher: null);

        var location = AssertSignedIn(response);
        Assert.That(location, Does.Contain("?r=").And.Contain("&ecc="), $"the page gets the sealed result: {location}");
    }

    [TestCase(null)]
    [TestCase("aes-gcm")]
    public async Task YouAuth400_TheHomeSitePayloadIsSealedAsTheStateDeclares(string? pageCipher)
    {
        var (response, pageKeyPair) = await SignInOnFrodosHomeSiteAsync(peerCipher: "", pageCipher);

        var location = AssertSignedIn(response);

        // ?r=<base64 payload>&ecc=<json>. Percent-decoded but not form-decoded: a '+' inside the
        // base64 is a '+', which is also how the public app must read it.
        var r = Uri.UnescapeDataString(location[(location.IndexOf("?r=", StringComparison.Ordinal) + 3)..location.IndexOf("&ecc=", StringComparison.Ordinal)]);
        var eccJson = Uri.UnescapeDataString(location[(location.IndexOf("&ecc=", StringComparison.Ordinal) + 5)..]);
        // The keys as the controller writes them and the public app reads them; every value is a string.
        var ecc = OdinSystemSerializer.Deserialize<Dictionary<string, string>>(eccJson)!;
        Assert.That(ecc.GetValueOrDefault("cipher"), Is.EqualTo(pageCipher ?? YouAuthDefaults.CipherAesCbc), $"ecc echoes what sealed the payload: {eccJson}");

        // Opened with the raw primitive, not the server's helper: the page is the other party here.
        var transferSecret = pageKeyPair.ExchangeSecretWith(ecc["pk"], ecc["salt"]);
        var iv = Convert.FromBase64String(ecc["iv"]);
        var cipherText = Convert.FromBase64String(r);
        var plain = pageCipher == YouAuthDefaults.CipherAesGcm
            ? AesGcm.Decrypt(cipherText, transferSecret, iv)
            : AesCbc.Decrypt(cipherText, transferSecret, iv);

        var payload = OdinSystemSerializer.Deserialize<Dictionary<string, string>>(plain.ToStringFromUtf8Bytes())!;
        Assert.That(payload.GetValueOrDefault("identity"), Is.EqualTo(Identities.Sam));
        Assert.That(Convert.FromBase64String(payload["ss64"]), Has.Length.EqualTo(16), "the browser's shared secret with Frodo's identity");
        Assert.That(payload.GetValueOrDefault("returnUrl"), Is.EqualTo("/"));
    }

    /// <summary>
    /// The browser's token from the home site (BuiltInBrowserApp, in XT32) is a guest's on V2, as it is on V1's guest
    /// routes: its access is what Sam's connection to Frodo allows, so nothing is granted that V1 doesn't already.
    /// </summary>
    [Test]
    public async Task TheHomeSiteTokenIsAGuestOnV2()
    {
        var (response, _) = await SignInOnFrodosHomeSiteAsync(peerCipher: "", pageCipher: null);
        AssertSignedIn(response);
        var xToken = GetSetCookieValue(response, YouAuthDefaults.XTokenCookieName);

        using var browser = Host.CreateClient();
        var verifyToken = $"https://{Identities.Frodo}/api/v2/auth/verify-token";

        var signedIn = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Get, verifyToken)
        {
            Headers = { { "Cookie", $"{YouAuthDefaults.XTokenCookieName}={xToken}" } }
        });
        Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the home site's token passes the guest policy");

        var anonymous = await browser.GetAsync(verifyToken);
        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "and without it the caller is anonymous, not a guest");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sam signs in on Frodo's home site. <paramref name="peerCipher"/> goes on the authorize
    /// request Sam's identity sees; <paramref name="pageCipher"/> is what Frodo's page declares in
    /// its state (null: an old bundle that declares nothing).
    /// </summary>
    private async Task<(HttpResponseMessage callbackResponse, KeyPair pageKeyPair)> SignInOnFrodosHomeSiteAsync(string peerCipher, string? pageCipher)
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var pageKeyPair = NewKeyPair();

        var request = DomainRequest(Identities.Frodo, FrodosCallback);
        request.PublicKey = await FrodosOfflineEccPublicKeyAsync();
        request.Cipher = peerCipher;
        request.State = OdinSystemSerializer.Serialize(new HomeAuthenticationState
        {
            FinalUrl = FrodosPage,
            ReturnUrl = "/",
            EccPk64 = pageKeyPair.PublicKeyJwk,
            Cipher = pageCipher
        });

        // Sam's identity sends the browser to Frodo's callback; the test plays the browser.
        var callback = await AuthorizeWithConsentAsync(Host, sam, request);

        using var browser = Host.CreateClient();
        var response = await browser.GetAsync(callback);
        return (response, pageKeyPair);
    }

    private async Task<string> FrodosOfflineEccPublicKeyAsync()
    {
        var response = await Host.AnonymousRefitFor<IPublicPrivateKeyHttpClientForOwner>(Identities.Frodo).GetEccOfflinePublicKey();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"offline_ecc: {response.StatusCode}");
        return response.Content!;
    }

    /// <summary>
    /// The whole value of a cookie the response sets; base64 padding included, which a split on '=' would drop.
    /// </summary>
    private static string GetSetCookieValue(HttpResponseMessage response, string name)
    {
        var prefix = name + "=";
        foreach (var header in response.Headers.GetValues("Set-Cookie"))
        {
            var pair = header.Split(';')[0];
            if (pair.StartsWith(prefix, StringComparison.Ordinal))
            {
                return pair[prefix.Length..];
            }
        }

        throw new AssertionException($"the response sets no {name} cookie");
    }

    private static string AssertSignedIn(HttpResponseMessage response)
    {
        var location = response.GetHeaderValue("Location") ?? "";
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), $"callback answers {(int)response.StatusCode}, location: {location}");
        Assert.That(location, Does.StartWith(FrodosPage));
        Assert.That(location, Does.Not.Contain("error="), $"the sign-in failed: {location}");

        var cookies = response.GetCookies();
        Assert.That(cookies, Does.ContainKey(YouAuthDefaults.XTokenCookieName), $"the browser is signed in on Frodo's site; cookies: {string.Join(", ", cookies.Keys)}");
        return location;
    }
}
