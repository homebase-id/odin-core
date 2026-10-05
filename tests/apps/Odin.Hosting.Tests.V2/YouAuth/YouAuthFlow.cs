#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// The YouAuth authorize flow driven over raw HTTP with the owner's cookie, for fixtures whose
/// subject is the Location header: the request, the consent POST, and what the redirects carry.
/// Plus the relying party's half, steps [010] and [090] to [140]: a key pair, and the token
/// exchange that opens what the redirect carried.
/// </summary>
internal static class YouAuthFlow
{
    /// <summary>The relying party's ECC key pair from step [010], kept so step [090] can derive the exchange secret.</summary>
    public sealed record KeyPair(SensitiveByteArray PrivateKey, EccFullKeyData FullKey)
    {
        public string PublicKeyJwk => FullKey.PublicKeyJwkBase64Url();

        /// <summary>YouAuth [090]: the exchange secret from our private key and the identity's public key and salt.</summary>
        public SensitiveByteArray ExchangeSecretWith(string identityPublicKeyJwk, string saltBase64) =>
            FullKey.GetEcdhSharedSecret(
                PrivateKey,
                EccPublicKeyData.FromJwkBase64UrlPublicKey(identityPublicKeyJwk),
                Convert.FromBase64String(saltBase64));
    }

    public static KeyPair NewKeyPair()
    {
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        return new KeyPair(privateKey, new EccFullKeyData(privateKey, EccKeySize.P384, 1));
    }

    public static string NewPublicKey() => NewKeyPair().PublicKeyJwk;

    /// <summary>
    /// A well-formed domain-client request; a test breaks the one field it is about. A test that
    /// goes on to exchange the token passes the key pair it will open it with.
    /// </summary>
    public static YouAuthAuthorizeRequest DomainRequest(string clientId, string redirectUri, KeyPair? keyPair = null) => new()
    {
        ClientId = clientId,
        ClientType = ClientType.domain,
        PublicKey = (keyPair ?? NewKeyPair()).PublicKeyJwk,
        State = "s",
        RedirectUri = redirectUri
    };

    /// <summary>GET authorize as the owner.</summary>
    public static async Task<HttpResponseMessage> AuthorizeAsync(OdinHost host, OwnerSession owner, YouAuthAuthorizeRequest payload)
    {
        var uri = new UriBuilder($"https://{owner.Identity.DomainName}{OwnerApiPathConstants.YouAuthV1Authorize}")
        {
            Query = payload.ToQueryString()
        }.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Headers = { { "Cookie", OwnerCookie(owner) } },
        };

        using var client = host.CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>
    /// The whole owner-side detour for a domain the owner has not consented to yet: authorize,
    /// consent, authorize again. Returns the callback URL the browser is sent to at [080], query
    /// and all.
    /// </summary>
    public static async Task<string> AuthorizeWithConsentAsync(OdinHost host, OwnerSession owner, YouAuthAuthorizeRequest payload)
    {
        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(host, owner, payload));
        await GiveConsentAsync(host, owner, returnUrl);

        var response = await AuthorizeAsync(host, owner, payload);
        AssertRedirectsTo(response, payload.RedirectUri.Split('?')[0]);
        return response.GetHeaderValue("Location")!;
    }

    /// <summary>
    /// YouAuth [090] to [140]: derive the exchange secret from what the callback URL carries, and
    /// swap its digest for the token at the identity's anonymous token endpoint.
    /// </summary>
    public static async Task<(YouAuthTokenResponse token, SensitiveByteArray exchangeSecret)> ExchangeTokenAsync(
        OdinHost host, string identity, KeyPair keyPair, string callbackUrl)
    {
        var callbackQuery = YouAuthTestHelper.ParseQueryString(callbackUrl);
        Assert.That(callbackQuery, Does.ContainKey(YouAuthDefaults.PublicKey), $"callback query: {string.Join(", ", callbackQuery.Keys)}");
        var exchangeSecret = keyPair.ExchangeSecretWith(callbackQuery[YouAuthDefaults.PublicKey], callbackQuery[YouAuthDefaults.Salt]);
        var digest = SHA256.HashData(exchangeSecret.GetKey()).ToBase64();

        var body = OdinSystemSerializer.Serialize(new YouAuthTokenRequest { SecretDigest = digest });
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{identity}{OwnerApiPathConstants.YouAuthV1Token}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var client = host.CreateClient();
        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"token endpoint: {content}");

        var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);
        Assert.That(token, Is.Not.Null, $"token response: {content}");
        return (token!, exchangeSecret);
    }

    /// <summary>
    /// The consent POST the consent page makes when the owner clicks OK, auto-approving for a month.
    /// </summary>
    public static async Task GiveConsentAsync(OdinHost host, OwnerSession owner, YouAuthAuthorizeRequest returnUrl)
    {
        var authorizeUrl = $"https://{owner.Identity.DomainName}{OwnerApiPathConstants.YouAuthV1Authorize}";
        var returnUri = new UriBuilder(authorizeUrl) { Query = returnUrl.ToQueryString() }.ToString();

        var consentRequirements = OdinSystemSerializer.Serialize(new ConsentRequirements
        {
            ConsentRequirementType = ConsentRequirementType.Expiring,
            Expiration = UnixTimeUtc.Now().AddDays(30)
        });

        var request = new HttpRequestMessage(HttpMethod.Post, authorizeUrl)
        {
            Headers = { { "Cookie", OwnerCookie(owner) } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUri },
                { YouAuthAuthorizeConsentGiven.ConsentRequirementName, consentRequirements },
            })
        };

        using var client = host.CreateClient();
        var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "consent given redirects back to authorize");
    }

    /// <summary>
    /// The response is the redirect to the consent page; returns the authorize request carried in
    /// its return URL, which is what the consent page reads.
    /// </summary>
    public static YouAuthAuthorizeRequest AssertRedirectsToConsent(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(new Uri(location).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent), $"expected the consent page, got {location}");

        var returnUrl = YouAuthTestHelper.ParseQueryString(location)["returnUrl"];
        return YouAuthAuthorizeRequest.FromQueryString(new Uri(returnUrl).Query);
    }

    /// <summary>
    /// The response is a redirect to the relying party's redirect URI; returns its query for the
    /// caller to assert on.
    /// </summary>
    public static Dictionary<string, string> AssertRedirectsTo(HttpResponseMessage response, string expectedUriWithoutQuery)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a failure after the redirect target is trusted goes back to the relying party");

        var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(location.Split('?')[0], Is.EqualTo(expectedUriWithoutQuery));

        return YouAuthTestHelper.ParseQueryString(location);
    }

    private static string OwnerCookie(OwnerSession owner) =>
        new Cookie(OwnerAuthConstants.CookieName, owner.Token.ToString()).ToString();
}
