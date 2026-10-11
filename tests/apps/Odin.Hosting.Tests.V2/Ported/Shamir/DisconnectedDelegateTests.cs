#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// A delegate disconnected from the dealer may still hold its shard, but cannot deliver it during
/// recovery: the dealer refuses it. So it must not count as usable, and approving its release must
/// fail visibly rather than report success (#1885).
/// </summary>
/// <remarks>
/// Its own fixture because the recovery test ends in a shard rotation that cannot reach the
/// disconnected delegate, which trips the tenant's <see cref="ShardRotationGate"/>; see
/// <see cref="FailedShardRotationTests"/>.
/// </remarks>
[TestFixture]
public class DisconnectedDelegateTests : ShamirFixture
{
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
    [
        // the rotation after recovery cannot send to the disconnected delegate
        "Cannot resolve client access token; not connected",
        "Failed to enqueue shards",
        "Failed to start shard rotation shards"
    ];

    [Test]
    public async Task DisconnectedDelegateShardIsNotCounted()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var gone = players[0];

        await PeerFlow.DisconnectAsync(gone, frodo);

        var verifyResponse = await SecurityOf(frodo).VerifyShards();
        Assert.That(verifyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var results = verifyResponse.Content!.Players;

        Assert.That(results[gone.Identity.DomainName].IsValid, Is.False, "a shard that cannot be delivered is not valid");
        Assert.That(results[gone.Identity.DomainName].IsConnected, Is.False);
        foreach (var player in players.Skip(1))
        {
            Assert.That(results[player.Identity.DomainName].IsValid, Is.True, player.Identity.DomainName);
            Assert.That(results[player.Identity.DomainName].IsConnected, Is.True, player.Identity.DomainName);
        }

        // the console's per-row check answers the same
        var goneEnvelope = config.Envelopes.Single(e => e.Player.OdinId == gone.Identity);
        var rowResponse = await SecurityOf(frodo).VerifyRemotePlayerShard(new VerifyRemotePlayerShardRequest
        {
            OdinId = gone.Identity,
            ShardId = goneEnvelope.ShardId
        });
        Assert.That(rowResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(rowResponse.Content!.IsValid, Is.False);
        Assert.That(rowResponse.Content.IsConnected, Is.False);

        // and the health check behind the risk report marks it, so it is not counted
        var healthResponse = await SecurityOf(frodo).RunRecoveryHealthCheck();
        Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var goneHealth = healthResponse.Content!.Players.Single(p => p.Player.OdinId == gone.Identity);
        Assert.That(goneHealth.IsValid, Is.False);
        Assert.That(goneHealth.IsConnected, Is.False);
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DisconnectedDelegateApprovalFailsAndKeepsTheRequest()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var gone = players[0];

        await PeerFlow.DisconnectAsync(gone, frodo);
        await EnterRecoveryModeAsync(frodo);

        var request = await GetPlayerShardRequestAsync(config, gone);
        Assert.That(request, Is.Not.Null, "the release request still reaches a disconnected delegate");

        var approveResponse = await SecurityOf(gone).ApproveShardRequest(new ApproveShardRequest
        {
            OdinId = frodo.Identity,
            ShardId = request!.ShardId
        });
        Assert.That(approveResponse.IsSuccessStatusCode, Is.False, "approval reported success though the shard was refused");
        Assert.That(await GetPlayerShardRequestAsync(config, gone), Is.Not.Null, "a failed approval must keep the request");

        // the three still connected are enough
        Assert.That(players.Count - 1, Is.GreaterThanOrEqualTo(config.MinMatchingShards));
        await ApproveEveryShardRequestAsync(frodo, players.Skip(1).ToList(), config);
        await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);
    }
}
