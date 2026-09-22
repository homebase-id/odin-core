using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// While an upgrade runs, every /api call gets a 503 -- except the one that says what the upgrade is
/// doing.
/// </summary>
/// <remarks>
/// Blocking that endpoint too made the only question worth asking during an upgrade the one question
/// that could not be asked. The guard itself is the point of the fixture, so this pins both halves:
/// an ordinary call is still refused, and version-info still answers.
/// <para>
/// <c>NonParallelizable</c> because the run state is a tenant singleton and this sets it: a fixture
/// running alongside on the same identity would see the 503s meant for this one.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class VersionUpgradeGuardTests : V2Fixture
{
    [Test]
    public async Task VersionInfoAnswersWhileTheRestIsRefused()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var runState = scope.Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        try
        {
            runState.SetRunning(true);

            var ordinary = await client.GetAsync("/api/owner/v1/circles/definitions/list");
            Assert.That(ordinary.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "an ordinary owner call must still be refused while an upgrade runs");
            Assert.That(ordinary.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True,
                "the refusal must say why");

            var versionInfo = await client.GetAsync("/api/owner/v1/data-conversion/data-version-info");
            Assert.That(versionInfo.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "version-info is how a client learns what the upgrade is doing");
            Assert.That(versionInfo.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True,
                "and it still carries the header, so the client knows an upgrade is in flight");
        }
        finally
        {
            runState.SetRunning(false);
        }

        // Back to normal once the upgrade is over.
        var after = await client.GetAsync("/api/owner/v1/circles/definitions/list");
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// The YouAuth code-for-token exchange is refused like anything else, and says why.
    /// </summary>
    /// <remarks>
    /// Pinned separately from the general case because two things now depend on this exact
    /// behaviour, and both break quietly if it changes.
    /// <para>
    /// This endpoint is not an incidental casualty: approving a YouAuth sign-in means logging into
    /// the owner console, and that login is what schedules the upgrade
    /// (<c>OwnerAuthenticationHandler</c> -&gt; <c>VersionUpgradeScheduler</c>). So the upgrade
    /// reliably begins between <c>authorize</c> and <c>token</c> -- the two halves of one sign-in
    /// land on either side of it.
    /// </para>
    /// <para>
    /// <c>HomeAuthenticationController.ExchangeDigestForToken</c> reads the header to tell "that
    /// identity is upgrading, try again shortly" apart from a genuine failure. Exempt this endpoint
    /// from the guard, or drop the header, and that becomes dead code with no test to say so.
    /// </para>
    /// <para>
    /// The real caller is another identity's server with no owner token; the guard runs ahead of
    /// authentication and does not distinguish, so the authenticated client here reaches it just
    /// the same.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheYouAuthTokenExchangeIsRefusedAndSaysWhy()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var runState = scope.Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        try
        {
            runState.SetRunning(true);

            var response = await client.PostAsync("/api/owner/v1/youauth/token",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "the token exchange must be refused while an upgrade runs");
            Assert.That(response.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True,
                "and must say why, which is the only thing that makes the failure distinguishable");
        }
        finally
        {
            runState.SetRunning(false);
        }
    }

    /// <summary>
    /// A browser navigated to an owner endpoint mid-upgrade is sent to the owner console's upgrade
    /// screen; everything else still gets the 503.
    /// </summary>
    /// <remarks>
    /// Signing in to a new app navigates the browser itself to <c>/api/owner/v1/youauth/authorize</c>,
    /// and the refusal above has no body -- so the user was shown a bare 503 by their browser. The
    /// consent screen's own upgrade check could not help: reaching that screen depends on this very
    /// request redirecting there.
    /// <para>
    /// The 503 is load-bearing for every other caller (see the token-exchange test above), so this
    /// pins both sides: a navigation redirects, a non-navigation on the same path does not.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ABrowserNavigationIsSentToTheUpgradeScreen()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var runState = scope.Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        try
        {
            runState.SetRunning(true);

            var navigation = new HttpRequestMessage(HttpMethod.Get, "/api/owner/v1/youauth/authorize");
            navigation.Headers.Add("Sec-Fetch-Dest", "document");
            navigation.Headers.Add("Accept", "text/html");

            var response = await client.SendAsync(navigation);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect),
                "a browser navigation must be sent somewhere that can explain itself");

            var location = response.Headers.Location?.ToString() ?? "";
            Assert.That(location, Does.StartWith("/owner/data-upgrade?returnUrl="),
                "and that somewhere is the screen which polls the upgrade to completion");
            Assert.That(WebUtility.UrlDecode(location), Does.Contain("/api/owner/v1/youauth/authorize"),
                "carrying where to go back to, so the sign-in resumes rather than being abandoned");

            var xhr = new HttpRequestMessage(HttpMethod.Get, "/api/owner/v1/youauth/authorize");
            xhr.Headers.Add("Sec-Fetch-Dest", "empty");

            var xhrResponse = await client.SendAsync(xhr);

            Assert.That(xhrResponse.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "a non-navigation on the same path must still be refused");
            Assert.That(xhrResponse.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True,
                "with the header its callers read to tell an upgrade apart from a failure");
        }
        finally
        {
            runState.SetRunning(false);
        }
    }

    /// <summary>
    /// Giving consent mid-upgrade lands on the upgrade screen too, pointed back at the sign-in it
    /// came from.
    /// </summary>
    /// <remarks>
    /// The consent screen submits a real form POST to the same authorize endpoint, so it is refused
    /// like the navigation before it. A form carries its parameters in the body, so coming back to
    /// the request URL would fail for want of a redirect_uri -- the resume target is the authorize
    /// URL the form was rendered for, which the form itself carries as <c>return_url</c> and which
    /// the consent handler redirects to on success. Re-rendering consent there costs one more click
    /// and keeps the sign-in alive.
    /// </remarks>
    [Test]
    public async Task ConsentGivenDuringAnUpgradeResumesAfterIt()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var runState = scope.Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        var authorizeUrl =
            $"https://{owner.Identity.DomainName}/api/owner/v1/youauth/authorize?client_id=someapp.example.com";

        try
        {
            runState.SetRunning(true);

            var consent = new HttpRequestMessage(HttpMethod.Post, "/api/owner/v1/youauth/authorize")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "return_url", authorizeUrl }
                })
            };
            consent.Headers.Add("Sec-Fetch-Dest", "document");

            var response = await client.SendAsync(consent);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther),
                "a posted form must be answered with a page to fetch, not a re-post");
            Assert.That(WebUtility.UrlDecode(response.Headers.Location?.ToString() ?? ""),
                Is.EqualTo($"/owner/data-upgrade?returnUrl={authorizeUrl}"),
                "and must carry the sign-in it interrupted, so the owner resumes where they were");

            // A return_url pointing somewhere else would make the upgrade screen an open redirect.
            var forged = new HttpRequestMessage(HttpMethod.Post, "/api/owner/v1/youauth/authorize")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "return_url", "https://evil.example.com/api/owner/v1/youauth/authorize" }
                })
            };
            forged.Headers.Add("Sec-Fetch-Dest", "document");

            var forgedResponse = await client.SendAsync(forged);

            Assert.That(forgedResponse.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "a resume target on another host is not one we hand to a browser");
        }
        finally
        {
            runState.SetRunning(false);
        }
    }
}
