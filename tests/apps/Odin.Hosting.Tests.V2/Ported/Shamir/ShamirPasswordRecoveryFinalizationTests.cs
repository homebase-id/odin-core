using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirPasswordRecoveryFinalizationTests</c>. The end of the recovery
/// road: once enough players release their shards — <see cref="PlayerType.Delegate"/>s on approval,
/// <see cref="PlayerType.Automatic"/> ones at once — the dealer's reassembled recovery key is mailed
/// to it, the owner sets a new password with it, and the next owner-authenticated request rotates
/// every shard, because the old ones are keyed to the old password.
/// </summary>
/// <remarks>
/// <para>Checked port. Shared arrange lives on <see cref="ShamirFixture"/>; its remarks carry the
/// outbox verdict, the drain points and the nonce-from-log reasoning.</para>
/// <para>
/// Decisions:
/// <list type="bullet">
/// <item>No caller matrix in the original and none added; the <c>#if !DEBUG [Ignore]</c> guards are
/// carried verbatim. The finalize nonce and the final recovery key are read out of the log for the
/// same reason as the enter/exit nonces — <c>RecoveryNotifier.EnqueueFinalizeRecoveryEmail</c> logs
/// them under <c>#if DEBUG</c> and no mail is sent.</item>
/// <item><c>Security.WaitForShamirStatus(AwaitingOwnerFinalization)</c> becomes a straight status
/// read; see <see cref="ShamirPasswordRecoveryTestForDelegates"/>'s remarks for why there is nothing
/// to wait for.</item>
/// <item><c>OldOwnerApi.CalculatePasswordReply</c> becomes the host-taking
/// <c>OwnerPasswordFlow</c>.<c>CalculatePasswordReplyAsync</c> overload, and
/// <c>LoginToOwnerConsole</c> + <c>OldOwnerApi.CreateOwnerApiHttpClient(id, cat, ss, …)</c> together
/// become one <see cref="OwnerSession.LoginAsync"/> against <c>NewPassword</c>. That session carries
/// the post-reset token and shared secret the assertions check, and its <c>RefitFor</c> replaces the
/// hand-built factory. <c>OwnerLogin.RunAsync</c> skips its set-password step because the baseline
/// already set one for this identity — so the login authenticates with <c>NewPassword</c>, which is
/// the point of the assertion.</item>
/// <item><c>OldOwnerApi.UpdateOwnerAuthContext(...)</c> is dropped. It existed to
/// repair the V1 scaffold's process-wide token cache after the password changed under it;
/// <c>V2Fixture</c> restores the identity DB — password included — before the next test, so there is
/// nothing to repair. This is lifecycle, which <c>V2Fixture</c> owns.</item>
/// <item>Trailing <c>CleanupConnections</c> calls are dropped as state restoration. The original's
/// comment on them ("before resetting the password so we can still use the old context") was already
/// stale: the call sat <i>after</i> the reset and the second login.</item>
/// <item><c>AssertHasDebugLogEvent(RotateShardsHasStarted, n)</c> counts rotations.
/// <c>OwnerAuthenticationHandler</c> calls <c>RotateShardKeysIfNeeded</c> on <i>every</i>
/// owner-authenticated request, and only a rotation that saved stops the next one (or
/// <c>ShardRotationGate</c>, while one is running or after one failed) — so the count is checked after
/// the first request since the password changed, and again after one more. The rotation itself is
/// synchronous inside that request.</item>
/// </list>
/// </para>
/// <para>
/// <b>Rotation coverage beyond the original (#1861):</b> the original's
/// <c>ShardingIsResetAfterPasswordIsRecovered</c> becomes
/// <see cref="ShardsAreRotatedAndRecoverTheIdentityAgain"/>, run for both player types. It drains the
/// rotated sends and runs <c>VerifyShards</c>, asserts a later request does not rotate again, and
/// recovers a second time from the rotated shards alone. <c>EnableAutoPasswordRecovery</c> deals to
/// <see cref="ShamirFixture.AutomatedPlayerIdentities"/>, so the host carries those too.
/// </para>
/// <para>
/// <b>Latent defect carried, not fixed:</b> the first test's name promises "…AndPasswordCanBeReset"
/// and it does reset the password, but the only proof it kept is that a fresh login with the new
/// password succeeds — it never checks the old password now fails.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirPasswordRecoveryFinalizationTests : ShamirFixture
{
    private const string SecondNewPassword = "bipbopboop2";

    protected override string[] HostIdentities =>
        base.HostIdentities.Union(AutomatedPlayerIdentities).ToArray();

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task DelegatePlayersCanApproveShardRequestsAndPasswordCanBeReset()
    {
        //
        // Setup - distribute delegate shards
        //
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();

        //
        // Act - enter recovery mode
        //
        await EnterRecoveryModeAsync(frodo);

        //
        // Assert - all player delegates have a request in their list
        //
        await ApproveEveryShardRequestAsync(frodo, players, config);

        await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);
    }

    /// <summary>
    /// Recovery rotates every shard on the next owner request — the automated case is #1861, where the
    /// dealer is not connected to its players — and the rotated shards alone recover the identity again.
    /// </summary>
    [TestCase(PlayerType.Delegate)]
    [TestCase(PlayerType.Automatic)]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task ShardsAreRotatedAndRecoverTheIdentityAgain(PlayerType playerType)
    {
        var (frodo, players) = Cast();
        var firstShardConfig = playerType == PlayerType.Delegate
            ? (await ArrangeDelegateShardsAsync()).Config
            : await ArrangeAutomatedShardsAsync(frodo);

        var recovered = await RecoverAsync(frodo, players, firstShardConfig, NewPassword);
        var rotatedShardConfig = await AssertShardsRotatedAsync(recovered, firstShardConfig, rotationsSoFar: 1);

        var recoveredAgain = await RecoverAsync(recovered, players, rotatedShardConfig, SecondNewPassword);
        await AssertShardsRotatedAsync(recoveredAgain, rotatedShardConfig, rotationsSoFar: 2);
    }

    /// <summary>
    /// The owner app fires several requests at once after a login; only one of them may rotate.
    /// </summary>
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task ConcurrentRequestsAfterRecoveryRotateOnce()
    {
        var (frodo, players, firstShardConfig) = await ArrangeDelegateShardsAsync();
        var recovered = await RecoverAsync(frodo, players, firstShardConfig, NewPassword);

        var configuration = recovered.RefitFor<IRefitOwnerConfiguration>();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => configuration.GetTenantSettings()));
        Assert.That(responses.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));

        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);
        var rotated = await GetDealerShardConfigAsync(recovered);
        Assert.That(rotated.Updated.milliseconds, Is.GreaterThan(firstShardConfig.Updated.milliseconds));
    }

    /// <summary>Turns on automated recovery. Note: no connections — production has none either.</summary>
    private static async Task<DealerShardConfig> ArrangeAutomatedShardsAsync(OwnerSession dealer)
    {
        var enableResponse = await dealer.RefitFor<IRefitOwnerConfiguration>().EnableAutoPasswordRecovery();
        Assert.That(enableResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await dealer.Sync.DrainOutboxAsync();

        var config = await GetDealerShardConfigAsync(dealer);
        Assert.That(config.UsesAutomaticRecovery, Is.True);
        return config;
    }

    /// <summary>
    /// Stokes the auth handler with one owner request, which rotates, and asserts the rotation: a
    /// newer config with the same players, types, threshold and mode but fresh shard ids, delivered
    /// to every player. Then asserts a further request does not rotate again.
    /// </summary>
    /// <param name="rotationsSoFar">Rotations expected in this test's log once this one has run.</param>
    private async Task<DealerShardConfig> AssertShardsRotatedAsync(
        OwnerSession recovered, DealerShardConfig previous, int rotationsSoFar)
    {
        var configuration = recovered.RefitFor<IRefitOwnerConfiguration>();

        var settingsResponse = await configuration.GetTenantSettings();
        Assert.That(settingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: rotationsSoFar);

        var rotated = await GetDealerShardConfigAsync(recovered);

        // UnixTimeUtc is not IComparable — Is.GreaterThan on it throws at run time.
        Assert.That(rotated.Updated.milliseconds, Is.GreaterThan(previous.Updated.milliseconds));
        Assert.That(rotated.UsesAutomaticRecovery, Is.EqualTo(previous.UsesAutomaticRecovery));
        Assert.That(rotated.MinMatchingShards, Is.EqualTo(previous.MinMatchingShards));
        Assert.That(rotated.Envelopes.Count, Is.EqualTo(previous.Envelopes.Count));

        foreach (var previousEnvelope in previous.Envelopes)
        {
            var rotatedEnvelope = rotated.Envelopes
                .SingleOrDefault(e => e.Player.OdinId == previousEnvelope.Player.OdinId);
            Assert.That(rotatedEnvelope, Is.Not.Null, $"no rotated envelope for {previousEnvelope.Player.OdinId}");
            Assert.That(rotatedEnvelope!.ShardId, Is.Not.EqualTo(previousEnvelope.ShardId));
            Assert.That(rotatedEnvelope.Player.Type, Is.EqualTo(previousEnvelope.Player.Type));
        }

        await DrainAndVerifyShardsAsync(recovered, previous.Envelopes.Count);

        // a rotation that saved is done; the handler must not rotate again
        await configuration.GetTenantSettings();
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: rotationsSoFar);
        Assert.That(await IsRotationPendingAsync(recovered), Is.False);

        return rotated;
    }
}
