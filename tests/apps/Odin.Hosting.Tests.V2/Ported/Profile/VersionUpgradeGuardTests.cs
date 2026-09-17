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
}
