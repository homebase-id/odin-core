using System.Net;
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
}
