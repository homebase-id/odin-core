using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.V2.Api;
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
/// <b>Not carried, and worth a reviewer's eye:</b> the ignored
/// <c>FailToDistributeWhenOneOrMorePeersIsNotConnected</c> registered a per-test
/// <c>SetAssertLogEventsAction</c> asserting exactly one Error event starting "Failed while creating
/// outbox item". <c>V2Fixture</c> has no per-test equivalent — only fixture-wide
/// <c>ToleratedErrorLogSubstrings</c> / <c>AssertLogEvents</c> — and adding a fixture-wide toleration
/// would blunt the invariant for the four tests that do run, to satisfy one that never does. The
/// registration is therefore dropped and flagged here instead; whoever un-ignores that test needs to
/// put the toleration back at the same time. Note the original's assertion was never once executed:
/// the <c>[Ignore]</c> predates it.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirRecoveryConfigurationTests : ShamirFixture
{
    [Test]
    public async Task CanDistributeShardsToDelegatePeersAndVerify()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        await PrepareConnectionsAsync(frodo, peerIdentities);

        await DistributeAndVerifyShardsAsync(frodo, peerIdentities, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.MinimumPlayerCount);
    }

    [Test]
    [Ignore("work in progress, need to setup testing so we have no recovery email")]
    public async Task FailShardDistributionWhenNoRecoveryEmailConfigured()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        await PrepareConnectionsAsync(frodo, peerIdentities);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            DelegateShardRequest(peerIdentities, minMatchingShards: peerIdentities.Count));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = configureShardsResponse.Error.ParseProblemDetails();
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.InvalidEmail),
            "should have been bad email due to missing recovery email");
    }

    [Test]
    public async Task FailShardDistributionWhenPlayerCountTooLow()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        await PrepareConnectionsAsync(frodo, peerIdentities);

        var tooFew = peerIdentities.Take(ShamirConfigurationService.MinimumPlayerCount - 1).ToList();
        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            DelegateShardRequest(tooFew, minMatchingShards: ShamirConfigurationService.MinimumPlayerCount - 1));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    [Ignore("Removed rule")]
    public async Task FailShardDistributionWhenMinMatchingShardsTooLow()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        await PrepareConnectionsAsync(frodo, peerIdentities);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            DelegateShardRequest(peerIdentities,
                minMatchingShards: peerIdentities.Count - (ShamirConfigurationService.MinimumMatchingShardsOffset + 1)));

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    [Ignore("dark launched")]
    public async Task FailToDistributeWhenOneOrMorePeersIsNotConnected()
    {
        // The original registered a per-test log-event assertion here — exactly one Error event
        // starting "Failed while creating outbox item". See the class remarks for why it is not
        // carried and what has to be restored alongside this [Ignore].

        var (frodo, allPeers) = await LoginCastAsync();

        var connectedIdentities = allPeers.Take(3).ToList();
        await PrepareConnectionsAsync(frodo, connectedIdentities);

        // add one who is not connected
        var peerIdentities = allPeers.ToList();

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(
            DelegateShardRequest(peerIdentities, minMatchingShards: peerIdentities.Count));

        Assert.That(configureShardsResponse.IsSuccessful, Is.False);
    }

    private static ConfigureShardsRequest DelegateShardRequest(
        IReadOnlyList<OwnerSession> players, int minMatchingShards) => new()
    {
        Players = players.Select(p => new ShamiraPlayer
        {
            OdinId = p.Identity,
            Type = PlayerType.Delegate
        }).ToList(),
        MinMatchingShards = minMatchingShards
    };
}
