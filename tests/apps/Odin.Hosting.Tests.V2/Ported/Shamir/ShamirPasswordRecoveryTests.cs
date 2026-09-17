using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirPasswordRecoveryTests</c>. Distributes shards to four
/// <see cref="PlayerType.Automatic"/> players, then walks the dealer in and back out of recovery
/// mode via the two emailed verification links.
/// </summary>
/// <remarks>
/// <para>Checked port. Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the
/// outbox verdict, the drain points and the nonce-from-log reasoning.</para>
/// <para>
/// Decisions:
/// <list type="bullet">
/// <item>No caller matrix in the original and none added — an <c>OwnerApi/</c> fixture, plain
/// <c>[Test]</c>.</item>
/// <item>The <c>#if !DEBUG [Ignore]</c> guard is carried verbatim. It is load-bearing twice over:
/// the whole flow depends on <c>RecoveryNotifier</c>'s <c>#if DEBUG</c> nonce logging, and on
/// <c>AssertEmailEnabled</c> not throwing while <c>Mailgun:Enabled</c> is false.</item>
/// <item><c>DriveRedux.WaitForEmptyOutbox(TransientTempDrive)</c> becomes
/// <c>dealer.Sync.DrainOutboxAsync()</c>, inside
/// <see cref="ShamirFixture.DistributeAndVerifyShardsAsync"/>.</item>
/// <item>The original's helper was called <c>DistributeAndVerifyAutomaticShards</c> but differs from
/// the delegates' copy only in <c>PlayerType</c> and in a hard-coded <c>MinMatchingShards = 3</c>;
/// both are passed in here, and both values are carried unchanged.</item>
/// </list>
/// </para>
/// <para>
/// <b>Carried defects in the original, both left alone:</b>
/// <list type="bullet">
/// <item><c>DistributeAndVerifyAutomaticShards</c> ended with a call to <c>CleanupConnections</c> —
/// so the entire recovery-mode flow this test is named for runs against <i>severed</i> connections.
/// That is not cosmetic and it is not cleanup, so it is carried: the mid-test disconnect stays
/// exactly where the original put it. It works because <c>request-shard</c> is authorised by the
/// peer perimeter policy (<c>IsInOdinNetwork</c>) and then by shard-owner identity, not by an
/// active connection. The sibling delegate fixtures' copy of the same helper has no such call, which
/// is the strongest sign it was a copy-and-paste slip rather than intent.</item>
/// <item>Because of the above, the test's own trailing <c>CleanupConnections</c> disconnected
/// already-disconnected peers and asserted success on it. That one is trailing state restoration,
/// which <c>V2Fixture</c>'s per-test reset owns, so it is dropped per the README.</item>
/// </list>
/// </para>
/// <para>
/// <b>Known flaky (docs/flakytests.md):</b> in CI on <c>windows/sqlite/debug</c> the V1 original has
/// been seen answering <c>Forbidden</c> where it expects <c>Redirect</c>. Ported faithfully; see the
/// batch report for the stability measurement here.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirPasswordRecoveryTests : ShamirFixture
{
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task CanEnterAndExitRecoveryMode()
    {
        var (frodo, players) = Cast();

        //
        // Setup - enter recovery mode
        //
        await PrepareConnectionsAsync(frodo, players);

        await DistributeAndVerifyShardsAsync(frodo, players, PlayerType.Automatic, minMatchingShards: 3);

        // Carried from the original's DistributeAndVerifyAutomaticShards, which severed every
        // connection before returning. See the class remarks.
        await CleanupConnectionsAsync(frodo, players);

        // enter recovery mode
        await EnterRecoveryModeAsync(frodo);

        // Act - exit recovery mode
        // Assert
        await ExitRecoveryModeAsync(frodo);
    }

    /// <summary>
    /// Severs both sides of the dealer/player connections. Note: no circles.
    /// </summary>
    /// <remarks>
    /// Private to this fixture rather than shared on <see cref="ShamirFixture"/>: this is the only
    /// test that severs anything, and it does so mid-test as a carried defect (see the class remarks),
    /// not as cleanup. The four <i>trailing</i> calls the other originals carried were dropped as
    /// state restoration, which <c>V2Fixture</c>'s per-test reset owns.
    /// </remarks>
    private static async Task CleanupConnectionsAsync(OwnerSession dealer, IEnumerable<OwnerSession> players)
    {
        foreach (var player in players)
        {
            await PeerFlow.DisconnectAsync(dealer, player);
        }
    }
}
