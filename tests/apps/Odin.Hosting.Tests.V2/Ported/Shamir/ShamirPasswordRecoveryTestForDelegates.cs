using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirPasswordRecoveryTestForDelegates</c>. What
/// <see cref="PlayerType.Delegate"/> players can do with the release request that lands in their
/// list when the dealer enters recovery mode: approve it (recovery reaches
/// <see cref="ShamirRecoveryState.AwaitingOwnerFinalization"/>), reject it (recovery stalls at
/// <see cref="ShamirRecoveryState.AwaitingSufficientDelegateConfirmation"/>), and reject it and then
/// have the dealer restart, which must mint a fresh request for every player.
/// </summary>
/// <remarks>
/// <para>Checked port. Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the
/// outbox verdict, the drain points and the nonce-from-log reasoning.</para>
/// <para>
/// Decisions:
/// <list type="bullet">
/// <item>No caller matrix in the original and none added; the <c>#if !DEBUG [Ignore]</c> guards and
/// the <c>[Description]</c> are carried verbatim.</item>
/// <item><c>Security.WaitForShamirStatus(AwaitingOwnerFinalization)</c> was a passive poll (40 s
/// budget, 100 ms ticks) and becomes a straight <c>GetShamirRecoverStatus()</c> read. Nothing here
/// is asynchronous to wait for: <c>ShamirRecoveryService.ApproveShardRequest</c> posts the shard to
/// the dealer over a <i>direct</i> peer call — <c>SendPlayerShard</c>, not the outbox — so by the
/// time the last approval returns, the dealer has already collected its quorum and flipped the
/// state. The two sibling tests in the same file read the status directly for exactly this reason;
/// this makes the third match them. Measured stable over the runs in the batch report.</item>
/// <item>Ported straight, <c>WaitForShamirStatus</c> would still have returned on its first tick —
/// but it is a poll of a value the fast host can never change on its own, which is the shape the
/// README rules out.</item>
/// <item>Trailing <c>CleanupConnections</c> calls are dropped as state restoration. The connections
/// are <i>not</i> severed mid-test here, and must not be: <c>ApproveShardRequest</c> builds its peer
/// client from the ICR (<c>GetIcrAsync</c> + <c>CreateClientAuthToken</c>), so an approval over a
/// severed connection would not authenticate.</item>
/// </list>
/// </para>
/// <para>
/// <b>Carried oddity:</b> the original's comment on the first test — "this is a dumb test but I just
/// wanted to be clear about success criterion" — sits above an assertion written as
/// <c>Assert.That(x == y)</c>, i.e. a precomputed bool with no constraint. It is rewritten as
/// <c>Is.EqualTo</c> so a failure prints both states; the claim is unchanged.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirPasswordRecoveryTestForDelegates : ShamirFixture
{
    [Test]
    [Description("When I configure shards with players that require approval before releasing a shard, " +
                 "that player can see a request from the dealer in a list")]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DelegatePlayersCanApproveShardRequests()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        //
        // Setup - distribute delegate shards
        //
        await PrepareConnectionsAsync(frodo, peerIdentities);

        await DistributeAndVerifyShardsAsync(frodo, peerIdentities, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.CalculateMinAllowedShardCount(peerIdentities.Count));

        var config = await GetDealerShardConfigAsync(frodo);

        //
        // Act - enter recovery mode
        //
        await EnterRecoveryModeAsync(frodo);

        //
        // Assert - all player delegates have a request in their list
        //
        foreach (var peer in peerIdentities)
        {
            var item = await GetPlayerShardRequestAsync(config, peer);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
            var shardId = item!.ShardId;

            // now release the shard
            var approveResponse = await SecurityOf(peer).ApproveShardRequest(new ApproveShardRequest
            {
                OdinId = frodo.Identity,
                ShardId = shardId
            });

            Assert.That(approveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Read the status: the approvals above delivered synchronously (see class remarks).
        var getRecoveryStatusResponse = await AnonymousSecurityOf(frodo).GetShamirRecoverStatus();
        Assert.That(getRecoveryStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var recoverStatus = getRecoveryStatusResponse.Content;

        // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
        Assert.That(recoverStatus!.State, Is.EqualTo(ShamirRecoveryState.AwaitingOwnerFinalization));

        await ExitRecoveryModeAsync(frodo);
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DelegatePlayersCanRejectRequestsAndRecoveryFails()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        //
        // Setup - distribute delegate shards
        //
        await PrepareConnectionsAsync(frodo, peerIdentities);

        await DistributeAndVerifyShardsAsync(frodo, peerIdentities, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.CalculateMinAllowedShardCount(peerIdentities.Count));

        var config = await GetDealerShardConfigAsync(frodo);

        //
        // Act - enter recovery mode
        //
        await EnterRecoveryModeAsync(frodo);

        //
        // Assert - all player delegates have a request in their list
        //
        foreach (var peer in peerIdentities)
        {
            var item = await GetPlayerShardRequestAsync(config, peer);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
            var shardId = item!.ShardId;

            // now release the shard
            var rejectResponse = await SecurityOf(peer).RejectShardRequest(new RejectShardRequest
            {
                OdinId = frodo.Identity,
                ShardId = shardId
            });

            Assert.That(rejectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Wait for status
        var getRecoveryStatusResponse = await AnonymousSecurityOf(frodo).GetShamirRecoverStatus();
        Assert.That(getRecoveryStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var recoverStatus = getRecoveryStatusResponse.Content;
        Assert.That(recoverStatus!.State, Is.EqualTo(ShamirRecoveryState.AwaitingSufficientDelegateConfirmation));

        await ExitRecoveryModeAsync(frodo);
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DelegatePlayersCanRejectRequestsAndRecoveryFailsThenResubmitRecoveryModeASecondTime()
    {
        var (frodo, peerIdentities) = await LoginCastAsync();

        //
        // Setup - distribute delegate shards
        //
        await PrepareConnectionsAsync(frodo, peerIdentities);

        await DistributeAndVerifyShardsAsync(frodo, peerIdentities, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.CalculateMinAllowedShardCount(peerIdentities.Count));

        var config = await GetDealerShardConfigAsync(frodo);

        //
        // Act - enter recovery mode
        //
        await EnterRecoveryModeAsync(frodo);

        //
        // Assert - all player delegates have a request in their list
        //
        foreach (var peer in peerIdentities)
        {
            var item = await GetPlayerShardRequestAsync(config, peer);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
            var shardId = item!.ShardId;

            // now release the shard
            var rejectResponse = await SecurityOf(peer).RejectShardRequest(new RejectShardRequest
            {
                OdinId = frodo.Identity,
                ShardId = shardId
            });

            Assert.That(rejectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Wait for status
        var getRecoveryStatusResponse = await AnonymousSecurityOf(frodo).GetShamirRecoverStatus();
        Assert.That(getRecoveryStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var recoverStatus = getRecoveryStatusResponse.Content;
        Assert.That(recoverStatus!.State, Is.EqualTo(ShamirRecoveryState.AwaitingSufficientDelegateConfirmation));

        //
        // If I don't get sufficient responses, i need to restart.
        // this should clear my responses
        await ExitRecoveryModeAsync(frodo);

        // re-enter recovery mode and validate delegates got a new request
        await EnterRecoveryModeAsync(frodo);

        foreach (var peer in peerIdentities)
        {
            var item = await GetPlayerShardRequestAsync(config, peer);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
        }
    }
}
