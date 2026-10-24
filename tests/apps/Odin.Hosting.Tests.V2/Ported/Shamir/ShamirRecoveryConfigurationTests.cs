#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirRecoveryConfigurationTests</c>. The rules <c>configure-shards</c>
/// enforces on a friend deal, which the browser deals (docs/zero-knowledge-recovery-plan.md, "Building
/// the friends pair"): the happy path, and the refusals.
/// </summary>
/// <remarks>
/// <para>Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the outbox verdict and
/// the drain points.</para>
/// <para>
/// Changed with the friends pair: the server no longer deals friends, so each request here is a deal
/// <see cref="ZkFriends"/> made.  Of the original's three <c>[Ignore]</c>s, the "Removed rule" one went
/// with its rule; the unconnected-player one runs now (the deal is refused while the shards are queued,
/// inside the request); the missing-recovery-email one is still ignored for its original reason.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirRecoveryConfigurationTests : ShamirFixture
{
    protected override System.Collections.Generic.IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
    [
        // the unconnected player's shard can't be queued
        "Failed while creating outbox item"
    ];

    [Test]
    public async Task CanDistributeShardsToDelegatePeersAndVerify()
    {
        var (frodo, players) = Cast();
        var config = await ArrangeFriendDealAsync(frodo, players.Select(p => (p, PlayerType.Delegate)).ToList(),
            minMatchingShards: ShamirConfigurationService.MinimumPlayerCount);

        Assert.That(config.UsesAutomaticRecovery, Is.False);
        Assert.That(config.Envelopes.Select(e => e.Player.OdinId), Is.EquivalentTo(players.Select(p => p.Identity)));
    }

    [Test]
    [Ignore("work in progress, need to setup testing so we have no recovery email")]
    public async Task FailShardDistributionWhenNoRecoveryEmailConfigured()
    {
        var (frodo, players) = Cast();
        var deal = await ZkFriends.MakeDealAsync(frodo, ByteArrayUtil.GetRndByteArray(16),
            players.Select(p => (p, PlayerType.Automatic)).ToList(), minMatchingShards: players.Count);

        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(deal.Request);

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(TestUtils.ParseProblemDetails(configureShardsResponse.Error!), Is.EqualTo(OdinClientErrorCode.InvalidEmail),
            "should have been bad email due to missing recovery email");
    }

    [Test]
    public async Task FailShardDistributionWhenPlayerCountTooLow()
    {
        var (frodo, players) = Cast();
        var tooFew = players.Take(ShamirConfigurationService.MinimumPlayerCount - 1).ToList();

        // Refused before anything is looked up, so no keys or connections are needed
        var deal = await ZkFriends.MakeDealAsync(frodo, ByteArrayUtil.GetRndByteArray(16),
            tooFew.Select(p => (p, PlayerType.Automatic)).ToList(), minMatchingShards: tooFew.Count);
        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(deal.Request);

        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), configureShardsResponse.Error?.Content);
    }

    /// <summary>F2: servers alone (the automatic friends) must never reach the threshold.</summary>
    [Test]
    public async Task ADealWhoseAutomaticShardsReachTheThresholdIsRefused()
    {
        var (frodo, players) = Cast();
        var deal = await ZkFriends.MakeDealAsync(frodo, ByteArrayUtil.GetRndByteArray(16),
            players.Select(p => (p, PlayerType.Automatic)).ToList(), minMatchingShards: 3);

        DriveAsserts.AssertRejected(await SecurityOf(frodo).ConfigureShards(deal.Request),
            OdinClientErrorCode.AutomaticShardsReachThreshold);
    }

    /// <summary>Change 6: until a friend's drive is zero knowledge, their server could open their shard.</summary>
    [Test]
    public async Task AGatedFriendWhoseDriveIsNotZeroKnowledgeIsRefused()
    {
        var (frodo, players) = Cast();
        await PrepareConnectionsAsync(frodo, players);
        await MakeZeroKnowledgeAsync(frodo);
        foreach (var player in players.Skip(1))
        {
            await MakeZeroKnowledgeAsync(player);
        }

        var deal = await ZkFriends.MakeDealAsync(frodo, ZkMasterKeyOf(frodo),
            players.Select(p => (p, PlayerType.Delegate)).ToList(), minMatchingShards: 3);

        DriveAsserts.AssertRejected(await SecurityOf(frodo).ConfigureShards(deal.Request),
            OdinClientErrorCode.ShardHolderNotZeroKnowledge);
        Assert.That((await SecurityOf(frodo).GetShardConfig()).Content, Is.Null, "nothing was dealt");
    }

    [Test]
    public async Task AGatedShardSealedToAnyOtherKeyIsRefused()
    {
        var (frodo, players) = Cast();
        await PrepareConnectionsAsync(frodo, players);
        foreach (var owner in players.Prepend(frodo))
        {
            await MakeZeroKnowledgeAsync(owner);
        }

        var deal = await ZkFriends.MakeDealAsync(frodo, ZkMasterKeyOf(frodo),
            players.Select(p => (p, PlayerType.Delegate)).ToList(), minMatchingShards: 3);
        var first = deal.Request.Shards[0];
        var (content, keyHeader) = ZkFriends.MakeDeposit(new PlayerEncryptedShard { Id = first.ShardId },
            ZkBrowser.MakeKeyPair().PublicKeyJwk);
        deal.Request.Shards[0] = first with { DepositContent = content, DepositKeyHeader = keyHeader };

        DriveAsserts.AssertRejected(await SecurityOf(frodo).ConfigureShards(deal.Request), OdinClientErrorCode.InvalidKeyHeader);
    }

    [Test]
    public async Task FailToDistributeWhenOneOrMorePeersIsNotConnected()
    {
        var (frodo, players) = Cast();
        var connected = players.Take(3).ToList();
        var notConnected = players[3];
        await PrepareConnectionsAsync(frodo, connected);
        foreach (var owner in connected.Prepend(frodo))
        {
            await MakeZeroKnowledgeAsync(owner);
        }

        // the one who isn't connected holds an automatic shard, so no key is fetched from them
        var deal = await ZkFriends.MakeDealAsync(frodo, ZkMasterKeyOf(frodo),
            connected.Select(p => (p, PlayerType.Delegate)).Append((notConnected, PlayerType.Automatic)).ToList(),
            minMatchingShards: 3);
        var configureShardsResponse = await SecurityOf(frodo).ConfigureShards(deal.Request);

        Assert.That((int)configureShardsResponse.StatusCode, Is.Not.InRange(200, 299), configureShardsResponse.Error?.Content);
        Assert.That((await SecurityOf(frodo).GetShardConfig()).Content, Is.Null, "the failed deal rolled back");
    }
}
