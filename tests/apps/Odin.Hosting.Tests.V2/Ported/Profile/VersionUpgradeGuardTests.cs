using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Version;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Services.Authentication.Owner;
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
    /// A browser navigated to the authorize endpoint mid-upgrade is sent to the owner console's
    /// upgrade screen, told where to come back to.
    /// </summary>
    /// <remarks>
    /// Signing in to an app navigates the browser itself to <c>authorize</c>, and the refusal above
    /// has no body -- so the owner was shown a bare 503 by their browser. The consent screen's own
    /// upgrade check could not help: reaching that screen depends on this very request.
    /// </remarks>
    [Test]
    public async Task ABrowserNavigationIsSentToTheUpgradeScreen()
    {
        await WhileUpgradingAsync(async client =>
        {
            var response = await client.GetAsync(OwnerApiPathConstants.YouAuthV1Authorize);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect),
                "a browser navigation must be sent somewhere that can explain itself");

            var location = response.Headers.Location?.ToString() ?? "";
            Assert.That(location, Does.StartWith($"{OwnerFrontendPathConstants.DataUpgrade}?returnUrl="),
                "and that somewhere is the screen which polls the upgrade to completion");
            Assert.That(WebUtility.UrlDecode(location), Does.Contain(OwnerApiPathConstants.YouAuthV1Authorize),
                "carrying where to go back to, so the sign-in resumes rather than being abandoned");
        });
    }

    /// <summary>
    /// Giving consent mid-upgrade lands on the upgrade screen too, pointed back at the sign-in it
    /// came from -- unless the form says to come back somewhere else entirely.
    /// </summary>
    /// <remarks>
    /// The consent screen submits a real form POST to the same endpoint. A form carries its
    /// parameters in the body, so coming back to the request URL would fail for want of a
    /// redirect_uri -- the resume target is the authorize URL the form was rendered for, which the
    /// form carries as <c>return_url</c> and which the consent handler redirects to on success.
    /// Re-rendering consent there costs one more click and keeps the sign-in alive.
    /// </remarks>
    [Test]
    public async Task ConsentGivenDuringAnUpgradeResumesAfterIt()
    {
        var authorizeUrl =
            $"https://{Identities.TomBombadil}{OwnerApiPathConstants.YouAuthV1Authorize}?client_id=someapp.example.com";

        await WhileUpgradingAsync(async client =>
        {
            var response = await client.SendAsync(ConsentPost(authorizeUrl));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther),
                "a posted form must be answered with a page to fetch, not a re-post");
            Assert.That(WebUtility.UrlDecode(response.Headers.Location?.ToString() ?? ""),
                Is.EqualTo($"{OwnerFrontendPathConstants.DataUpgrade}?returnUrl={authorizeUrl}"),
                "and must carry the sign-in it interrupted, so the owner resumes where they were");

            // A return_url pointing somewhere else would make the upgrade screen an open redirect.
            var forged = await client.SendAsync(
                ConsentPost($"https://evil.example.com{OwnerApiPathConstants.YouAuthV1Authorize}"));

            Assert.That(forged.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "a resume target on another host is not one we hand to a browser");
        });
    }

    private static HttpRequestMessage ConsentPost(string returnUrl) =>
        new(HttpMethod.Post, OwnerApiPathConstants.YouAuthV1Authorize)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl }
            })
        };

    /// <summary>
    /// Runs <paramref name="body"/> against an owner client while the identity reports an upgrade in
    /// progress, and clears the flag afterwards however it ends.
    /// </summary>
    /// <remarks>
    /// The run state is a tenant singleton, so a test that left it set would hand its 503s to every
    /// other fixture on this identity -- which is what <c>NonParallelizable</c> is guarding. One
    /// place that knows to reset it beats four.
    /// </remarks>
    private async Task WhileUpgradingAsync(Func<HttpClient, Task> body)
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var runState = Host.GetTenantScope(owner.Identity.DomainName).Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        runState.SetRunning(true);
        try
        {
            await body(client);
        }
        finally
        {
            runState.SetRunning(false);
        }
    }

    /// <summary>
    /// Version-info names what the upgrade is doing, not just whether the version is behind.
    /// </summary>
    /// <remarks>
    /// The screen that waits out an upgrade has to tell "running, keep waiting" from "over, go
    /// back", and the version number alone cannot: it is written before the run ends. Pinned here
    /// because the client's whole wait-and-resume flow hangs off this one value.
    /// </remarks>
    [Test]
    public async Task VersionInfoNamesWhatTheUpgradeIsDoing()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var runState = Host.GetTenantScope(owner.Identity.DomainName).Resolve<VersionUpgradeRunState>();
        var svc = owner.RefitFor<IVersionTestHttpClientForOwner>();

        try
        {
            runState.SetRunning(true);

            var during = await svc.GetVersionInfo();
            Assert.That(during.Content?.UpgradeState, Is.EqualTo(UpgradeState.Running),
                "while the job runs, the one endpoint left open must say so");
        }
        finally
        {
            runState.SetRunning(false);
        }

        var after = await svc.GetVersionInfo();
        Assert.That(after.Content?.UpgradeState, Is.EqualTo(UpgradeState.UpToDate),
            "and once it is over, that the identity has nothing left to do");
    }

    /// <summary>
    /// The "are you an identity?" probe answers while an upgrade runs.
    /// </summary>
    /// <remarks>
    /// Every login box opens with <c>GET /api/guest/v1/auth/ident</c> and reads <c>odinId</c> out of
    /// the body (<c>YouAuthLoginBox.pingIdentity</c>, <c>login-app/loginBox.ts</c>, the provisioning
    /// app). A bodiless 503 fails that parse, and each of those callers turns a parse failure into
    /// "identity not found" -- so refusing this made an upgrading identity look like it does not
    /// exist, to the person trying to sign in with it and to anyone checking the name.
    /// <para>
    /// Anonymous on purpose: the real caller is another identity's browser with no token here.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheIdentityProbeAnswersWhileAnUpgradeRuns()
    {
        await WhileUpgradingAsync(async _ =>
        {
            using var anonymous = Host.CreateClient();
            anonymous.BaseAddress = new Uri($"https://{Identities.TomBombadil}/");

            var response = await anonymous.GetAsync(GuestApiPathConstantsV1.IdentV1);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "an upgrading identity must still be able to say that it is an identity");

            var body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain(Identities.TomBombadil).IgnoreCase,
                "and name itself, which is the field every login box compares against what was typed");
            Assert.That(response.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True,
                "while still carrying the header, so a caller that wants to say 'busy' can");
        });
    }

    /// <summary>
    /// Health answers while an upgrade runs.
    /// </summary>
    /// <remarks>
    /// <c>/api/v2/health/ping</c> names the host and <c>/ip</c> names the caller; neither reads
    /// tenant data. Refused, a monitor or a client probing the host reports it down, which is not
    /// what "deliberately busy for a few seconds" means -- and the 503 reached a person as
    /// "server error" during a sign-in.
    /// </remarks>
    [Test]
    public async Task HealthAnswersWhileAnUpgradeRuns()
    {
        await WhileUpgradingAsync(async _ =>
        {
            using var anonymous = Host.CreateClient();
            anonymous.BaseAddress = new Uri($"https://{Identities.TomBombadil}/");

            var ping = await anonymous.GetAsync($"{UnifiedApiRouteConstants.Health}/ping");

            Assert.That(ping.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "a host that is upgrading is busy, not down");
            Assert.That(await ping.Content.ReadAsStringAsync(), Does.Contain(Identities.TomBombadil).IgnoreCase,
                "and still names itself");
        });
    }
}
