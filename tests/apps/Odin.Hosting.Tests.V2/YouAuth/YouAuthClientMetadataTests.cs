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
using Odin.Core.Util;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// A relying party's <c>/.well-known/youauth-client.json</c> at work: an identity serves one about
/// itself, and an identity authorizing a login reads the other party's to name it on the consent
/// page, store that name on the registration, and pin the callback to the paths it published.
/// See docs/youauth-client-metadata-plan.md; step numbers in the names are the flow diagram's.
/// </summary>
/// <remarks>
/// Sam is the relying party for the name tests, because his identity serves a document and the test
/// host routes Frodo's fetch of it in-process. A made-up site with a canned document, served by the
/// test host's fetch factory, is the relying party for the callback pin, since an identity's own
/// document publishes no callback list. A throwaway domain stands in for a site with no document.
/// </remarks>
[TestFixture]
public class YouAuthClientMetadataTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string SamsName = "Samwise Gamgee";

    /// <summary>The home-site login's callback: what a peer signing in on Sam's home site redirects to.</summary>
    private static string SamsCallback =>
        $"https://{Identities.Sam}{HomeApiPathConstants.AuthV1}/{HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName}";

    private const string PinnedSite = "pinned-site.org";
    private const string PinnedSiteCallback = $"https://{PinnedSite}/auth/callback";

    [OneTimeSetUp]
    public void PublishThePinnedSite()
    {
        InProcessDynamicHttpClientFactory.CannedDocuments[PinnedSite] =
            $$"""{"name":"Pinned Site","redirect_uris":["{{PinnedSiteCallback}}"]}""";
    }

    // ---------------------------------------------------------------------------------------
    // The identity's own document
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task AnIdentityServesItsOwnDocumentAnonymously()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);

        using var anonymous = Host.CreateAnonymousClient(Identities.Sam);
        var response = await anonymous.GetAsync(YouAuthDefaults.ClientMetadataPath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));

        var doc = OdinSystemSerializer.Deserialize<YouAuthClientMetadataDocument>(await response.Content.ReadAsStringAsync())!;
        Assert.That(doc.Name, Is.EqualTo(SamsName), "the owner's public name is what peers see on their consent page");
        Assert.That(doc.Logo, Is.EqualTo($"https://{Identities.Sam}/pub/image"), "the public image the consent page already fetches");
        Assert.That(doc.RedirectUris, Is.Null, "an identity host has one relying party on it, itself; a pin would protect nothing");
    }

    [Test]
    public async Task AnIdentityWithNoPublicProfileStillServesItsLogo()
    {
        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        var response = await anonymous.GetAsync(YouAuthDefaults.ClientMetadataPath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = OdinSystemSerializer.Deserialize<YouAuthClientMetadataDocument>(await response.Content.ReadAsStringAsync())!;
        Assert.That(doc.Logo, Is.EqualTo($"https://{Identities.Frodo}/pub/image"));
        Assert.That(doc.Name, Is.Null.Or.Empty, "no profile, no name; the document is still worth serving");
    }

    // ---------------------------------------------------------------------------------------
    // Reading the other party's document during authorize
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task YouAuth030_TheRedirectDomainsNameReachesTheConsentPage()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var response = await Authorize(frodo, SamAsRelyingParty(SamsCallback));

        var returnUrl = AssertRedirectsToConsent(response);
        Assert.That(returnUrl.ClientInfo, Is.EqualTo(SamsName),
            "the name the redirect domain gives itself, fetched from that domain, not what the query string said");

        var consentQuery = YouAuthTestHelper.ParseQueryString(response.GetHeaderValue("Location")!);
        Assert.That(consentQuery.GetValueOrDefault(YouAuthDefaults.ClientLogo), Is.EqualTo($"https://{Identities.Sam}/pub/image"));
    }

    [Test]
    public async Task YouAuth030_WhatTheQueryStringSaysIsNotTheName()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = SamAsRelyingParty(SamsCallback);
        request.ClientInfo = "Amazon";

        var returnUrl = AssertRedirectsToConsent(await Authorize(frodo, request));
        Assert.That(returnUrl.ClientInfo, Is.EqualTo(SamsName), "anyone can put anything in a link; only the domain's own word counts");
    }

    [Test]
    public async Task YouAuth055_TheNameIsStoredOnTheRegistration()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = SamAsRelyingParty(SamsCallback);
        var returnUrl = AssertRedirectsToConsent(await Authorize(frodo, request));
        await GiveConsentAsync(frodo, returnUrl);

        // Consent given; the flow re-enters authorize and completes.
        var completed = await Authorize(frodo, request);
        Assert.That(completed.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(completed.GetHeaderValue("Location"), Does.StartWith(SamsCallback));

        var registration = await frodo.RefitFor<IRefitYouAuthDomainRegistration>()
            .GetRegisteredDomain(new GetYouAuthDomainRequest { Domain = Identities.Sam });
        Assert.That(registration.IsSuccessStatusCode, Is.True, $"GetRegisteredDomain: {registration.StatusCode}");
        Assert.That(registration.Content!.Name, Is.EqualTo(SamsName),
            "the owner's list of connected domains shows the name, not only the host");
    }

    [Test]
    public async Task YouAuth030_ARedirectPathTheDomainDidNotPublishIsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = SamAsRelyingParty($"https://{PinnedSite}/somewhere/else");
        request.ClientId = PinnedSite;

        var response = await Authorize(frodo, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "the domain said which path is its callback; another path on the same host is not trusted");
    }

    [Test]
    public async Task YouAuth030_TheRelyingPartysQueryDoesNotBreakThePin()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = SamAsRelyingParty($"{PinnedSiteCallback}?session=abc");
        request.ClientId = PinnedSite;

        var returnUrl = AssertRedirectsToConsent(await Authorize(frodo, request));
        Assert.That(returnUrl.ClientInfo, Is.EqualTo("Pinned Site"));
    }

    [Test]
    public async Task YouAuth030_ADomainWithNoDocumentBehavesAsToday()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        const string domain = "amazoom.org";

        var request = SamAsRelyingParty($"https://{domain}/any/path/at/all");
        request.ClientId = domain;

        var returnUrl = AssertRedirectsToConsent(await Authorize(frodo, request));
        Assert.That(returnUrl.ClientInfo, Is.Empty, "no document, no name: the consent page shows the bare domain");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static async Task PublishNameAsync(OwnerSession owner, string name)
    {
        var response = await owner.V1.StaticFiles.PublishPublicProfileCard(new PublishPublicProfileCardRequest
        {
            ProfileCardJson = OdinSystemSerializer.Serialize(new { name })
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"PublishPublicProfileCard: {response.StatusCode}");
    }

    private static YouAuthAuthorizeRequest SamAsRelyingParty(string redirectUri) => new()
    {
        ClientId = Identities.Sam,
        ClientType = ClientType.domain,
        PublicKey = NewPublicKey(),
        State = "s",
        RedirectUri = redirectUri
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
    /// The consent POST the consent page makes when the owner clicks OK, auto-approving for a month.
    /// </summary>
    private async Task GiveConsentAsync(OwnerSession owner, YouAuthAuthorizeRequest returnUrl)
    {
        var returnUri = new UriBuilder($"https://{owner.Identity.DomainName}{OwnerApiPathConstants.YouAuthV1Authorize}")
        {
            Query = returnUrl.ToQueryString()
        }.ToString();

        var consentRequirements = OdinSystemSerializer.Serialize(new ConsentRequirements
        {
            ConsentRequirementType = ConsentRequirementType.Expiring,
            Expiration = UnixTimeUtc.Now().AddDays(30)
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{owner.Identity.DomainName}{OwnerApiPathConstants.YouAuthV1Authorize}")
        {
            Headers = { { "Cookie", new Cookie(OwnerAuthConstants.CookieName, owner.Token.ToString()).ToString() } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUri },
                { YouAuthAuthorizeConsentGiven.ConsentRequirementName, consentRequirements },
            })
        };

        using var client = Host.CreateClient();
        var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "consent given redirects back to authorize");
    }

    /// <summary>
    /// The response is the redirect to the consent page; returns the authorize request carried in
    /// its return URL, which is what the consent page reads.
    /// </summary>
    private static YouAuthAuthorizeRequest AssertRedirectsToConsent(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
        Assert.That(new Uri(location).AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent), $"expected the consent page, got {location}");

        var returnUrl = YouAuthTestHelper.ParseQueryString(location)["returnUrl"];
        return YouAuthAuthorizeRequest.FromQueryString(new Uri(returnUrl).Query);
    }
}
