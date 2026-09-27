#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Auth;
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
        var ecc = OdinSystemSerializer.Deserialize<EccInfo>(eccJson);
        Assert.That(ecc, Is.Not.Null, eccJson);
        Assert.That(ecc!.Cipher, Is.EqualTo(pageCipher ?? YouAuthDefaults.CipherAesCbc), $"ecc echoes what sealed the payload: {eccJson}");

        var transferSecret = pageKeyPair.ExchangeSecretWith(ecc.Pk!, ecc.Salt!);
        var iv = Convert.FromBase64String(ecc.Iv!);
        var cipherText = Convert.FromBase64String(r);
        var plain = pageCipher == YouAuthDefaults.CipherAesGcm
            ? AesGcm.Decrypt(cipherText, transferSecret, iv)
            : AesCbc.Decrypt(cipherText, transferSecret, iv);

        var payload = OdinSystemSerializer.Deserialize<Payload>(plain.ToStringFromUtf8Bytes());
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Identity, Is.EqualTo(Identities.Sam));
        Assert.That(Convert.FromBase64String(payload.Ss64!), Has.Length.EqualTo(16), "the browser's shared secret with Frodo's identity");
        Assert.That(payload.ReturnUrl, Is.EqualTo("/"));
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

        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, sam, request));
        await GiveConsentAsync(Host, sam, returnUrl);

        var authorized = await AuthorizeAsync(Host, sam, request);
        var callback = authorized.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(callback, Does.StartWith(FrodosCallback), "Sam's identity sends the browser to Frodo's callback");

        using var browser = Host.CreateClient();
        var response = await browser.GetAsync(callback);
        return (response, pageKeyPair);
    }

    private async Task<string> FrodosOfflineEccPublicKeyAsync()
    {
        using var client = Host.CreateClient();
        var response = await client.GetAsync($"https://{Identities.Frodo}{GuestApiPathConstantsV1.PublicKeysV1}/offline_ecc");
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), content);
        return content.Trim('"');
    }

    private static string AssertSignedIn(HttpResponseMessage response)
    {
        var location = response.GetHeaderValue("Location") ?? "";
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), $"callback answers {(int)response.StatusCode}, location: {location}");
        Assert.That(location, Does.StartWith(FrodosPage));
        Assert.That(location, Does.Not.Contain("error="), $"the sign-in failed: {location}");

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
        Assert.That(cookies, Has.Some.StartsWith($"{YouAuthDefaults.XTokenCookieName}="), $"the browser is signed in on Frodo's site; cookies: {string.Join(" | ", cookies)}");
        return location;
    }

    private sealed class EccInfo
    {
        public string? Pk { get; set; }
        public string? Salt { get; set; }
        public string? Iv { get; set; }
        public string? Cipher { get; set; }
    }

    private sealed class Payload
    {
        public string? Identity { get; set; }
        public string? Ss64 { get; set; }
        public string? ReturnUrl { get; set; }
    }
}
