#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Plan A converts every owner's Shard Recovery drive at login, long before Shamir moves into the
/// browser (plan B).  Today's server-dealt Shamir must keep working on converted drives: its files are
/// unencrypted, so it never needs the drive key the server no longer has.
/// </summary>
[TestFixture]
public class ShamirOnZeroKnowledgeDrivesTests : ShamirFixture
{
    [Test]
    public async Task DelegateRecoveryWorksWhenEveryShardRecoveryDriveIsConverted()
    {
        var (frodo, players) = Cast();
        foreach (var owner in players.Prepend(frodo))
        {
            var keys = await ZkOwnerSteps.CreateKeysAsync(owner, OwnerLogin.DefaultPassword, "login keys".ToUtf8ByteArray());
            await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);
        }

        var (dealer, _, config) = await ArrangeDelegateShardsAsync();
        await EnterRecoveryModeAsync(dealer);
        await ApproveEveryShardRequestAsync(dealer, players, config);
        await AssertRecoveryStateAsync(dealer, ShamirRecoveryState.AwaitingOwnerFinalization);

        var recovered = await FinalizeRecoveryAndLoginAsync(dealer, NewPassword);

        // The reset used the recovery key, so the zero-knowledge keys locked by the forgotten password
        // are gone; the browser makes new ones at the next login and converts the drive again.
        var status = await ZkOwnerSteps.GetStatusAsync(recovered);
        Assert.That(status.ZkSaltKek64, Is.Null);

        var newKeys = await ZkOwnerSteps.CreateKeysAsync(recovered, NewPassword, "login keys".ToUtf8ByteArray());
        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(recovered, newKeys.MasterKey);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(recovered)).ShardRecoveryDriveIsZeroKnowledge, Is.True);
    }
}
