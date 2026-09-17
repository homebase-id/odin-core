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
/// <item>The one ordering constraint that is load-bearing and is preserved:
/// <c>AssertHasDebugLogEvent(RotateShardsHasStarted, 1)</c> counts exactly one rotation.
/// <c>OwnerAuthenticationHandler</c> calls <c>RotateShardKeysIfNeeded</c> on <i>every</i>
/// owner-authenticated request, so the assertion only holds while exactly one such request has been
/// made since the password changed — hence the single <c>GetTenantSettings</c> call before it, as in
/// the original. The rotation itself is synchronous inside that request.</item>
/// </list>
/// </para>
/// <para>
/// <b>Latent defect carried, not fixed:</b> the rotation enqueues four fresh shard sends into the
/// dealer's outbox and nothing drains them, so the second test asserts on the rotated <i>config</i>
/// without ever confirming the rotated shards reached the players. That is what the original
/// asserted and it is left alone; a drain-plus-<c>VerifyShards</c> here would be new coverage.
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
        //
        // Setup - distribute delegate shards
        //
        var (frodo, players, firstShardConfig) = await ArrangeDelegateShardsAsync();

        //
        // Act - enter recovery mode
        //
        await EnterRecoveryModeAsync(frodo);

        //
        // Assert - all player delegates have a request in their list
        //
        await ApproveEveryShardRequestAsync(frodo, players, firstShardConfig);

        await FinalizeRecoveryWithNewPasswordAsync(frodo);

        //login with the new password
        var recovered = await OwnerSession.LoginAsync(Host, Identities.Frodo, NewPassword);
        Assert.That(recovered.Token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(recovered.Token.AccessTokenHalfKey.IsSet(), Is.True);
        Assert.That(recovered.SharedSecret.IsSet(), Is.True);

        // the system should configure shards again
        // so lets call something on the owner console to stoke the auth handler
        // just a random call
        var settingsResponse = await recovered.RefitFor<IRefitOwnerConfiguration>().GetTenantSettings();
        Assert.That(settingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // now just see if the log is updated with an entry that we rotated shards
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        // test that the shards were rotated
        var getConfigResponse2 = await SecurityOf(recovered).GetShardConfig();
        Assert.That(getConfigResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondShardingConfig = getConfigResponse2.Content;
        Assert.That(secondShardingConfig, Is.Not.Null);

        // UnixTimeUtc is not IComparable — Is.GreaterThan on it throws at run time.
        Assert.That(secondShardingConfig!.Updated.milliseconds,
            Is.GreaterThan(firstShardConfig.Updated.milliseconds));

        Assert.That(firstShardConfig.Envelopes.Count, Is.EqualTo(secondShardingConfig.Envelopes.Count));
        Assert.That(firstShardConfig.MinMatchingShards, Is.EqualTo(secondShardingConfig.MinMatchingShards));

        foreach (var firstEnvelope in firstShardConfig.Envelopes)
        {
            var secondEnvelope = secondShardingConfig.Envelopes
                .SingleOrDefault(e => e.Player.OdinId == firstEnvelope.Player.OdinId);
            Assert.That(secondEnvelope, Is.Not.Null, $"no rotated envelope for {firstEnvelope.Player.OdinId}");
            Assert.That(secondEnvelope!.ShardId, Is.Not.EqualTo(firstEnvelope.ShardId));
            Assert.That(secondEnvelope.Player.Type, Is.EqualTo(firstEnvelope.Player.Type));
        }
    }

    /// <summary>
    /// Reads the finalize nonce + final recovery key out of the log, then posts the new password
    /// against them. Shared verbatim between the two tests.
    /// </summary>
    private async Task FinalizeRecoveryWithNewPasswordAsync(OwnerSession dealer)
    {
        // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
        await AssertRecoveryStateAsync(dealer, ShamirRecoveryState.AwaitingOwnerFinalization);

        // scan for the nonceId
        var finalizeNonceId = ReadLogPropertyValue(RecoveryNotifier.FinalRecoveryNonceIdPropertyName);
        var finalRecoveryKey = ReadLogPropertyValue(RecoveryNotifier.FinalRecoveryKeyPropertyName);

        Assert.That(finalizeNonceId, Is.Not.Null.Or.Empty, "Could not find final recovery email link");
        Assert.That(finalRecoveryKey, Is.Not.Null.Or.Empty, "Could not find final recovery email link");

        var saltyReply = await OwnerPasswordFlow.CalculatePasswordReplyAsync(
            Host, dealer.Identity.DomainName, NewPassword);

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
