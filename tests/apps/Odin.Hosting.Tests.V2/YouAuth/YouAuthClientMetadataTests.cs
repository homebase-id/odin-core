#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.YouAuth;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// A relying party's <c>/.well-known/youauth-client.json</c> at work: an identity serves one about
/// itself, and an identity authorizing a login reads the other party's to name it on the consent
/// page, store that name on the registration, and pin the callback to the paths it published.
/// See docs/youauth-client-metadata-plan.md; step numbers in the names are the flow diagram's.
/// </summary>
/// <remarks>
/// Sam is the relying party for the name tests: his identity serves a document and the test host
/// routes Frodo's fetch of it in-process. A made-up site with a canned document is the relying party
/// for the callback pin, since an identity's own document publishes no callback list. A throwaway
/// domain stands in for a site with no document.
/// </remarks>
[TestFixture]
public class YouAuthClientMetadataTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string SamsName = "Samwise Gamgee";
    private static string SamsLogo => $"https://{Identities.Sam}/pub/image";
    private static string SamsCallback => $"https://{Identities.Sam}/api/guest/v1/builtin/home/auth/auth-code-callback";

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

        var doc = await OwnDocumentAsync(Identities.Sam);
        Assert.That(doc.Name, Is.EqualTo(SamsName), "the owner's public name is what peers see on their consent page");
        Assert.That(doc.Logo, Is.EqualTo(SamsLogo), "the public image the consent page already fetches");
        Assert.That(doc.RedirectUris, Is.Null, "an identity host has one relying party on it, itself; a pin would protect nothing");
    }

    [Test]
    public async Task AnIdentityWithNoPublicProfileStillServesItsLogo()
    {
        var doc = await OwnDocumentAsync(Identities.Frodo);
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

        var response = await AuthorizeAsync(Host, frodo, DomainRequest(Identities.Sam, SamsCallback));

        var returnUrl = AssertRedirectsToConsent(response);
        Assert.That(returnUrl.ClientInfo, Is.EqualTo(SamsName),
            "the name the redirect domain gives itself, fetched from that domain, not what the query string said");

        var consentQuery = YouAuthTestHelper.ParseQueryString(response.GetHeaderValue("Location")!);
        Assert.That(consentQuery.GetValueOrDefault(YouAuthDefaults.ClientLogo), Is.EqualTo(SamsLogo));
    }

    [Test]
    public async Task YouAuth030_WhatTheQueryStringSaysIsNotTheName()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest(Identities.Sam, SamsCallback);
        request.ClientInfo = "Amazon";

        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, frodo, request));
        Assert.That(returnUrl.ClientInfo, Is.EqualTo(SamsName), "anyone can put anything in a link; only the domain's own word counts");
    }

    [Test]
    public async Task YouAuth055_TheNameIsStoredOnTheRegistration()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        await PublishNameAsync(sam, SamsName);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var request = DomainRequest(Identities.Sam, SamsCallback);
        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, frodo, request));
        await GiveConsentAsync(Host, frodo, returnUrl);

        // Consent given; the flow re-enters authorize and completes.
        var completed = await AuthorizeAsync(Host, frodo, request);
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

        var response = await AuthorizeAsync(Host, frodo, DomainRequest(PinnedSite, $"https://{PinnedSite}/somewhere/else"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "the domain said which path is its callback; another path on the same host is not trusted");
    }

    [Test]
    public async Task YouAuth030_TheRelyingPartysQueryDoesNotBreakThePin()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, frodo, DomainRequest(PinnedSite, $"{PinnedSiteCallback}?session=abc")));
        Assert.That(returnUrl.ClientInfo, Is.EqualTo("Pinned Site"));
    }

    [Test]
    public async Task YouAuth030_ADomainWithNoDocumentBehavesAsToday()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, frodo, DomainRequest("amazoom.org", "https://amazoom.org/any/path/at/all")));
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

    private async Task<YouAuthClientMetadataDocument> OwnDocumentAsync(string identity)
    {
        using var anonymous = Host.CreateAnonymousClient(identity);
        var response = await anonymous.GetAsync(YouAuthDefaults.ClientMetadataPath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        return OdinSystemSerializer.Deserialize<YouAuthClientMetadataDocument>(await response.Content.ReadAsStringAsync())!;
    }
}
