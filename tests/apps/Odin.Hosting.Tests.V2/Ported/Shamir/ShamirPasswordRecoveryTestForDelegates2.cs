using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirPasswordRecoveryTestForDelegates2</c>. Entering recovery mode
/// puts a shard-release request in every <see cref="PlayerType.Delegate"/> player's list — and this
/// fixture stops there, without approving or rejecting any of them.
/// </summary>
/// <remarks>
/// <para>Checked port. Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the
/// outbox verdict, the drain points and the nonce-from-log reasoning.</para>
/// <para>
/// Kept as its own fixture rather than folded into
/// <see cref="ShamirPasswordRecoveryTestForDelegates"/>, which it almost duplicates: the README
/// permits merging only where the difference between fixtures is already a caller, and this is a
/// difference in what the test does after entering recovery mode. Merging it would also make its
/// name — the only record that <c>...ForDelegates2</c> existed — vanish.
/// </para>
/// <para>
/// Decisions:
/// <list type="bullet">
/// <item>No caller matrix in the original and none added; the <c>#if !DEBUG [Ignore]</c> guard and
/// the <c>[Description]</c> are carried verbatim.</item>
/// <item>The original inlined the shard-request lookup in the test body <i>and</i> carried a private
/// <c>GetPlayerShardRequest</c> copy that nothing called. The inline lookup asserts exactly what
/// <see cref="ShamirFixture.GetPlayerShardRequestAsync"/> asserts, so it uses the shared helper; the
/// dead private copy is dropped.</item>
/// <item>The trailing <c>CleanupConnections</c> call is dropped as state restoration.</item>
/// </list>
/// </para>
/// </remarks>
[TestFixture]
public class ShamirPasswordRecoveryTestForDelegates2 : ShamirFixture
{
    [Test]
    [Description("When I configure shards with players that require approval before releasing a shard, " +
                 "that player can see a request from the dealer in a list")]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DelegatePlayersCanSeeShardReleaseRequests()
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
        }

        await ExitRecoveryModeAsync(frodo);
    }
}
