using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Util;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirRecoveryConfigurationTests</c>. The rules
/// <c>configure-shards</c> enforces before it will split a recovery key across delegate players:
/// the happy path, and the refusals.
/// </summary>
/// <remarks>
/// <para>Checked port. Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the
/// outbox verdict and the drain points.</para>
/// <para>
/// Decisions:
/// <list type="bullet">
/// <item>No caller matrix in the original and none added.</item>
/// <item>All three <c>[Ignore]</c>s are carried verbatim, reasons included — including
/// <c>FailShardDistributionWhenMinMatchingShardsTooLow</c>, whose rule is commented out in
/// <c>ShamirConfigurationService.ConfigureShardsInternal</c> today ("Removed rule"), and
/// <c>FailShardDistributionWhenNoRecoveryEmailConfigured</c>, which cannot run while the dev
/// registration seeds every tenant an email ("work in progress").</item>
/// <item>The four refusal tests never reach the outbox, so they have no drain; only
/// <c>CanDistributeShardsToDelegatePeersAndVerify</c> does, via
/// <see cref="ShamirFixture.DistributeAndVerifyShardsAsync"/>.</item>
/// <item>Trailing <c>CleanupConnections</c> calls are dropped — pure state restoration, which
/// <c>V2Fixture</c>'s per-test reset owns. Nothing they asserted is reachable only through them.</item>
/// </list>
/// </para>
/// <para>
/// <b>Per-test log expectation (#1767):</b> the ignored
/// <c>FailToDistributeWhenOneOrMorePeersIsNotConnected</c> registered a per-test
/// <c>SetAssertLogEventsAction</c> asserting exactly one Error event starting "Failed while creating
/// outbox item". It is carried as <see cref="V2Fixture.ExpectErrorLogEvent"/>, so it applies to that
/// test alone and is live the day the <c>[Ignore]</c> comes off. The original's assertion never once
/// executed: the <c>[Ignore]</c> predates it.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirRecoveryConfigurationTests : ShamirFixture
{
    [Test]
    public async Task CanDistributeShardsToDelegatePeersAndVerify()
    {
        var (frodo, players) = Cast();

        await PrepareConnectionsAsync(frodo, players);

        await DistributeAndVerifyShardsAsync(frodo, players, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.MinimumPlayerCount);
    }

    [Test]
    [Ignore("work in progress, need to setup testing so we have no recovery email")]
    public async Task FailShardDistributionWhenNoRecoveryEmailConfigured()
    {
        var (frodo, players) = Cast();

        await PrepareConnectionsAsync(frodo, players);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            ShardRequest(players, PlayerType.Delegate, minMatchingShards: players.Count));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = configureShardsResponse.Error.ParseProblemDetails();
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.InvalidEmail),
            "should have been bad email due to missing recovery email");
    }

    [Test]
    public async Task FailShardDistributionWhenPlayerCountTooLow()
    {
        var (frodo, players) = Cast();

        await PrepareConnectionsAsync(frodo, players);

        var tooFew = players.Take(ShamirConfigurationService.MinimumPlayerCount - 1).ToList();
        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            ShardRequest(tooFew, PlayerType.Delegate,
                minMatchingShards: ShamirConfigurationService.MinimumPlayerCount - 1));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    [Ignore("Removed rule")]
    public async Task FailShardDistributionWhenMinMatchingShardsTooLow()
    {
        var (frodo, players) = Cast();

        await PrepareConnectionsAsync(frodo, players);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            ShardRequest(players, PlayerType.Delegate,
                minMatchingShards: players.Count - (ShamirConfigurationService.MinimumMatchingShardsOffset + 1)));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    [Ignore("dark launched")]
    public async Task FailToDistributeWhenOneOrMorePeersIsNotConnected()
    {
        ExpectErrorLogEvent("Failed while creating outbox item");

        var (frodo, players) = Cast();

        var connectedIdentities = players.Take(3).ToList();
        await PrepareConnectionsAsync(frodo, connectedIdentities);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            // the full cast, which adds one who is not connected
            ShardRequest(players, PlayerType.Delegate, minMatchingShards: players.Count));

        // Exactly !IsSuccessStatusCode, but a failure prints the code it got.
        Assert.That((int)configureShardsResponse.StatusCode, Is.Not.InRange(200, 299));
    }
}
