using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Serilog.Events;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// Port of <c>OwnerApi/Shamir/ShamirPasswordRecoveryFinalizationTests</c>. The end of the recovery
/// road: once enough <see cref="PlayerType.Delegate"/> players approve, the dealer's reassembled
/// recovery key is mailed to it, the owner sets a new password with it — and the next
/// owner-authenticated request rotates every shard, because the old ones are keyed to the old
/// password.
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
/// <see cref="OwnerPasswordFlow"/>.<c>CalculatePasswordReplyAsync</c> overload, and
/// <c>LoginToOwnerConsole</c> + <c>OldOwnerApi.CreateOwnerApiHttpClient(id, cat, ss, …)</c> together
/// become one <see cref="OwnerSession.LoginAsync"/> against <c>NewPassword</c>. That session carries
/// the post-reset token and shared secret the assertions check, and its <c>RefitFor</c> replaces the
/// hand-built factory. <c>OwnerLogin.RunAsync</c> skips its set-password step because the baseline
/// already set one for this identity — so the login authenticates with <c>NewPassword</c>, which is
/// the point of the assertion.</item>
/// <item><c>OldOwnerApi.UpdateOwnerAuthContext(...)</c> is dropped from both tests. It existed to
/// repair the V1 scaffold's process-wide token cache after the password changed under it;
/// <c>V2Fixture</c> restores the identity DB — password included — before the next test, so there is
/// nothing to repair. This is lifecycle, which <c>V2Fixture</c> owns.</item>
/// <item>Trailing <c>CleanupConnections</c> calls are dropped as state restoration. The original's
/// comment on them ("before resetting the password so we can still use the old context") was already
/// stale: in both tests the call sits <i>after</i> the reset and the second login.</item>
/// <item><c>AssertHasDebugLogEvent(RotateShardsHasStarted, n)</c> counts rotations.
/// <c>OwnerAuthenticationHandler</c> calls <c>RotateShardKeysIfNeeded</c> on <i>every</i>
/// owner-authenticated request, and only a rotation that saved stops the next one — so the count is
/// checked after the first request since the password changed, and again after several more. The
/// rotation itself is synchronous inside that request.</item>
/// </list>
/// </para>
/// <para>
/// <b>Rotation coverage beyond the original (#1861):</b> every rotation test drains the rotated
/// sends and runs <c>VerifyShards</c>, asserts later requests do not rotate again, and has an
/// automated twin; the <c>Rotated…RecoverTheIdentityAgain</c> pair recovers a second time from the
/// rotated shards alone.
/// </para>
/// <para>
/// <b>Latent defect carried, not fixed (2):</b> the first test's name promises "…AndPasswordCanBeReset"
/// and it does reset the password, but the only proof it kept is that a fresh login with the new
/// password succeeds — it never checks the old password now fails.
/// </para>
/// </remarks>
[TestFixture]
public class ShamirPasswordRecoveryFinalizationTests : ShamirFixture
{
    private const string NewPassword = "bipbopboop";
    private const string SecondNewPassword = "bipbopboop2";

    /// <summary>
    /// <c>EnableAutoPasswordRecovery</c> deals to the four identities in
    /// <c>AccountRecovery:AutomatedPasswordRecoveryIdentities</c>. Collab is the one the shared cast
    /// does not already host.
    /// </summary>
    protected override string[] HostIdentities => [.. base.HostIdentities, Identities.Collab];

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

        await FinalizeRecoveryWithNewPasswordAsync(frodo);

        //login with the new password
        var recovered = await OwnerSession.LoginAsync(Host, Identities.Frodo, NewPassword);
        Assert.That(recovered.Token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(recovered.Token.AccessTokenHalfKey.IsSet(), Is.True);
        Assert.That(recovered.SharedSecret.IsSet(), Is.True);
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task ShardingIsResetAfterPasswordIsRecovered()
    {
        var (frodo, players, firstShardConfig) = await ArrangeDelegateShardsAsync();

        await EnterRecoveryModeAsync(frodo);
        await ApproveEveryShardRequestAsync(frodo, players, firstShardConfig);
        var recovered = await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);

        await AssertShardsRotatedAsync(recovered, firstShardConfig, rotationsSoFar: 1);
    }

    /// <summary>
    /// The automated twin of <see cref="ShardingIsResetAfterPasswordIsRecovered"/> (#1861). The dealer
    /// is not connected to the automated players, so the rotation must go out the automated way;
    /// sent down the delegate path it fails "not connected", rolls back, and retries on every request.
    /// </summary>
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task AutomatedShardingIsResetAfterPasswordIsRecovered()
    {
        var (frodo, _) = Cast();
        var firstShardConfig = await ArrangeAutomatedShardsAsync(frodo);

        // automated players release their shards without approval
        await EnterRecoveryModeAsync(frodo);
        var recovered = await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);

        await AssertShardsRotatedAsync(recovered, firstShardConfig, rotationsSoFar: 1);
    }

    /// <summary>
    /// A rotated config that merely <i>looks</i> right proves nothing; this recovers a second time
    /// from the rotated shards alone.
    /// </summary>
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task RotatedDelegateShardsRecoverTheIdentityAgain()
    {
        var (frodo, players, firstShardConfig) = await ArrangeDelegateShardsAsync();

        await EnterRecoveryModeAsync(frodo);
        await ApproveEveryShardRequestAsync(frodo, players, firstShardConfig);
        var recovered = await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);
        var rotatedShardConfig = await AssertShardsRotatedAsync(recovered, firstShardConfig, rotationsSoFar: 1);

        await EnterRecoveryModeAsync(recovered);
        await ApproveEveryShardRequestAsync(recovered, players, rotatedShardConfig);
        var recoveredAgain = await FinalizeRecoveryAndLoginAsync(recovered, SecondNewPassword);

        await AssertShardsRotatedAsync(recoveredAgain, rotatedShardConfig, rotationsSoFar: 2);
    }

    /// <summary>The automated twin of <see cref="RotatedDelegateShardsRecoverTheIdentityAgain"/>.</summary>
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task RotatedAutomatedShardsRecoverTheIdentityAgain()
    {
        var (frodo, _) = Cast();
        var firstShardConfig = await ArrangeAutomatedShardsAsync(frodo);

        await EnterRecoveryModeAsync(frodo);
        var recovered = await FinalizeRecoveryAndLoginAsync(frodo, NewPassword);
        var rotatedShardConfig = await AssertShardsRotatedAsync(recovered, firstShardConfig, rotationsSoFar: 1);

        await EnterRecoveryModeAsync(recovered);
        var recoveredAgain = await FinalizeRecoveryAndLoginAsync(recovered, SecondNewPassword);

        await AssertShardsRotatedAsync(recoveredAgain, rotatedShardConfig, rotationsSoFar: 2);
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
    /// to every player. Then asserts further requests do not rotate again.
    /// </summary>
    /// <param name="rotationsSoFar">Rotations expected in this test's log once this one has run.</param>
    private async Task<DealerShardConfig> AssertShardsRotatedAsync(
        OwnerSession recovered, DealerShardConfig previous, int rotationsSoFar)
    {
        var settingsResponse = await recovered.RefitFor<IRefitOwnerConfiguration>().GetTenantSettings();
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

        await recovered.Sync.DrainOutboxAsync();

        var verifyShardsResponse = await SecurityOf(recovered).VerifyShards();
        Assert.That(verifyShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verifyShardsResponse.Content!.Players.Where(p => !p.Value.IsValid).Select(p => p.Key), Is.Empty,
            "one or more players not holding the rotated shard");

        // a rotation that saved is done; the handler must not rotate again on later requests
        for (var i = 0; i < 3; i++)
        {
            await recovered.RefitFor<IRefitOwnerConfiguration>().GetTenantSettings();
        }

        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: rotationsSoFar);

        return rotated;
    }

    /// <summary>Finalizes recovery with <paramref name="newPassword"/> and logs in with it.</summary>
    private async Task<OwnerSession> FinalizeRecoveryAndLoginAsync(OwnerSession dealer, string newPassword)
    {
        await FinalizeRecoveryWithNewPasswordAsync(dealer, newPassword);

        var recovered = await OwnerSession.LoginAsync(Host, Identities.Frodo, newPassword);
        Assert.That(recovered.Token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(recovered.Token.AccessTokenHalfKey.IsSet(), Is.True);
        Assert.That(recovered.SharedSecret.IsSet(), Is.True);
        return recovered;
    }

    /// <summary>
    /// Reads the finalize nonce + final recovery key out of the log, then posts the new password
    /// against them.
    /// </summary>
    private async Task FinalizeRecoveryWithNewPasswordAsync(OwnerSession dealer, string newPassword = NewPassword)
    {
        // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
        await AssertRecoveryStateAsync(dealer, ShamirRecoveryState.AwaitingOwnerFinalization);

        // scan for the nonceId
        var finalizeNonceId = ReadLogPropertyValue(RecoveryNotifier.FinalRecoveryNonceIdPropertyName);
        var finalRecoveryKey = ReadLogPropertyValue(RecoveryNotifier.FinalRecoveryKeyPropertyName);

        Assert.That(finalizeNonceId, Is.Not.Null.Or.Empty, "Could not find final recovery email link");
        Assert.That(finalRecoveryKey, Is.Not.Null.Or.Empty, "Could not find final recovery email link");

        var saltyReply = await OwnerPasswordFlow.CalculatePasswordReplyAsync(
            Host, dealer.Identity.DomainName, newPassword);

        // here we will call finalize to get the recovery key
        var finalizeRecoveryResponse = await AnonymousSecurityOf(dealer).FinalizeRecovery(new FinalRecoveryRequest
        {
            Id = finalizeNonceId,
            FinalKey = finalRecoveryKey,
            PasswordReply = saltyReply
        });

        Assert.That(finalizeRecoveryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// <c>WebScaffold.AssertHasDebugLogEvent</c>: exactly <paramref name="count"/> Debug events whose
    /// rendered message equals <paramref name="message"/>. Private here rather than shared on
    /// <see cref="ShamirFixture"/> — this is the only Shamir test that reads the Debug channel.
    /// </summary>
    private void AssertHasDebugLogEvent(string message, int count)
    {
        var matching = Host.LogStore.GetLogEvents()[LogEventLevel.Debug]
            .Where(l => l.RenderMessage() == message)
            .ToList();

        Assert.That(matching, Has.Count.EqualTo(count), $"Debug log events matching '{message}'");
    }
}
