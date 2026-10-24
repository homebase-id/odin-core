#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Server-dealt Shamir around the recovery phrase takeover (docs/zero-knowledge-recovery-plan.md,
/// decision 6).  <c>Development:DealsLockThePhrase</c> makes every deal here lock the phrase, as every
/// deal did before recovery in the browser, until the takeover: so these start where every existing
/// owner is.
/// </summary>
[TestFixture]
public class RecoveryPhraseShamirTests : ShamirFixture
{
    protected override string[] HostIdentities =>
        base.HostIdentities.Union(AutomatedPlayerIdentities).ToArray();

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?> { ["Development:DealsLockThePhrase"] = "true" };

    /// <summary>
    /// Owners who forgot their password never log in, so never take over: their old deal must still
    /// recover, through the phrase the server still holds.
    /// </summary>
    [TestCase(PlayerType.Delegate)]
    [TestCase(PlayerType.Automatic)]
    public async Task AnOldDealStillRecoversBeforeTheTakeover(PlayerType playerType)
    {
        var (dealer, players) = Cast();
        var config = await ArrangeAsync(dealer, playerType);

        await RecoverAsync(dealer, players, config, NewPassword);
    }

    [TestCase(PlayerType.Delegate)]
    [TestCase(PlayerType.Automatic)]
    public async Task AnOldDealIsDealtAgainAtTheTakeoverAndRecoversAfterIt(PlayerType playerType)
    {
        var (dealer, players) = Cast();
        var before = await ArrangeAsync(dealer, playerType);

        var phrase = await TakeOverAsync(dealer);

        var after = await GetDealerShardConfigAsync(dealer);
        Assert.That(after.Updated.milliseconds, Is.GreaterThan(before.Updated.milliseconds));
        Assert.That(after.Envelopes.Select(e => e.Player.OdinId), Is.EquivalentTo(before.Envelopes.Select(e => e.Player.OdinId)));
        Assert.That(after.Envelopes.Select(e => e.ShardId), Has.None.AnyOf(before.Envelopes.Select(e => e.ShardId).ToArray()),
            "every shard is new");
        Assert.That(after.MinMatchingShards, Is.EqualTo(before.MinMatchingShards));
        Assert.That(after.UsesAutomaticRecovery, Is.EqualTo(before.UsesAutomaticRecovery));
        await DrainAndVerifyShardsAsync(dealer, before.Envelopes.Count);

        var recovered = await RecoverAsync(dealer, players, after, NewPassword);

        // The reset forgot the zero-knowledge keys and every copy tied to them; the phrase is still
        // valid, and the owner types it in once against the new keys.
        var keys = await ZkOwnerSteps.CreateKeysAsync(recovered, NewPassword);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(recovered)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.NeedsReentry));
        DriveAsserts.AssertRejected(await SecurityOf(recovered).RequestRecoveryKey(), OdinClientErrorCode.RecoveryPhraseNeedsReentry);

        // The re-deal after a password change ran on those requests, without a phrase to read.
        Assert.That(await IsRotationPendingAsync(recovered), Is.False);
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        var otherPhrase = BIP39Util.GenerateBIP39(ByteArrayUtil.GetRndByteArray(16));
        var mistyped = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(otherPhrase, keys.MasterKey));
        DriveAsserts.AssertRejected(mistyped, OdinClientErrorCode.RecoveryPhraseMismatch);

        var typed = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(phrase, keys.MasterKey));
        Assert.That(typed.StatusCode, Is.EqualTo(HttpStatusCode.OK), typed.Error?.Content);

        var recovery = await Host.AnonymousRefitFor<IOwnerAuthenticationClient>(recovered.Identity.DomainName).GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(NewPassword));
    }

    /// <summary>
    /// A friend the owner is no longer connected to can't be dealt to, so the old deal can't be
    /// repeated.  It's removed rather than block the takeover, and the security page says so.
    /// </summary>
    [Test]
    public async Task AnOldDealThatCannotBeDealtAgainIsRemovedAndTheSecurityPageSaysSo()
    {
        var (dealer, players, _) = await ArrangeDelegateShardsAsync();
        Assert.That((await RecoveryInfoAsync(dealer)).IsConfigured, Is.True);
        await PeerFlow.DisconnectAsync(players[0], dealer);
        ExpectErrorLogEvent("Failed while creating outbox item"); // the re-deal can't send to that friend

        await TakeOverAsync(dealer);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(dealer)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver));
        Assert.That((await RecoveryInfoAsync(dealer)).IsConfigured, Is.False, "the old deal is gone");
        var needsAttention = await PlainSecurityOf(dealer).GetNeedsAttention();
        Assert.That(needsAttention.StatusCode, Is.EqualTo(HttpStatusCode.OK), needsAttention.Error?.Content);
        Assert.That(needsAttention.Content!.NeedsAttention, Is.True, "the red dot on the security page");
    }

    private async Task<DealerShardConfig> ArrangeAsync(OwnerSession dealer, PlayerType playerType) =>
        playerType == PlayerType.Delegate
            ? (await ArrangeDelegateShardsAsync()).Config
            : await ArrangeAutomatedShardsAsync(dealer);

    /// <summary>The login's takeover, from a fresh password login as the browser does it.</summary>
    private async Task<string> TakeOverAsync(OwnerSession dealer)
    {
        var fresh = await OwnerSession.LoginAsync(Host, dealer.Identity.DomainName);
        var keys = await ZkOwnerSteps.CreateKeysAsync(fresh, OwnerLogin.DefaultPassword);
        return await ZkOwnerSteps.TakeOverPhraseAsync(fresh, OwnerLogin.DefaultPassword, keys.MasterKey);
    }
}
