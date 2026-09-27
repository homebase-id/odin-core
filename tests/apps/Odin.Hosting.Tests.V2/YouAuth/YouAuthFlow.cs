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
/// </summary>
internal static class YouAuthFlow
{
    public static string NewPublicKey()
    {
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        return new EccFullKeyData(privateKey, EccKeySize.P384, 1).PublicKeyJwkBase64Url();
    }

    /// <summary>A well-formed domain-client request; a test breaks the one field it is about.</summary>
    public static YouAuthAuthorizeRequest DomainRequest(string clientId, string redirectUri) => new()
    {
        ClientId = clientId,
        ClientType = ClientType.domain,
        PublicKey = NewPublicKey(),
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
