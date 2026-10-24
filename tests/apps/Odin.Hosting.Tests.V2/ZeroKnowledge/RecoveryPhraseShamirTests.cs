#nullable enable
using System.Collections.Generic;
using Autofac;
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
    [Test]
    public async Task AnOldDealStillRecoversBeforeTheTakeover()
    {
        var (dealer, _) = Cast();
        var config = await ArrangeAutomatedShardsAsync(dealer);

        await RecoverAutomatedAsync(dealer, config, NewPassword);
    }

    [Test]
    public async Task AnOldDealIsDealtAgainAtTheTakeoverAndRecoversAfterIt()
    {
        var (dealer, _) = Cast();
        var before = await ArrangeAutomatedShardsAsync(dealer);

        var phrase = await TakeOverAsync(dealer);

        var after = await GetDealerShardConfigAsync(dealer);
        Assert.That(after.Updated.milliseconds, Is.GreaterThan(before.Updated.milliseconds));
        Assert.That(after.Envelopes.Select(e => e.Player.OdinId), Is.EquivalentTo(before.Envelopes.Select(e => e.Player.OdinId)));
        Assert.That(after.Envelopes.Select(e => e.ShardId), Has.None.AnyOf(before.Envelopes.Select(e => e.ShardId).ToArray()),
            "every shard is new");
        Assert.That(after.MinMatchingShards, Is.EqualTo(before.MinMatchingShards));
        Assert.That(after.UsesAutomaticRecovery, Is.EqualTo(before.UsesAutomaticRecovery));
        await DrainAndVerifyShardsAsync(dealer, before.Envelopes.Count);

        var recovered = await RecoverAutomatedAsync(dealer, after, NewPassword);

        // The reset forgot the zero-knowledge keys and every copy tied to them; the phrase is still
        // valid, and the owner types it in once against the new keys.
        var keys = await ZkOwnerSteps.CreateKeysAsync(recovered, NewPassword);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(recovered)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.NeedsReentry));
        var unusable = await Host.AnonymousRefitFor<IOwnerAuthenticationClient>(recovered.Identity.DomainName).GetPhraseRecovery();
        Assert.That(unusable.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "the forgot-password page must say why the phrase can't recover");
        DriveAsserts.AssertRejected(await SecurityOf(recovered).RequestRecoveryKey(), OdinClientErrorCode.RecoveryPhraseNeedsReentry);

        // The re-deal after a password change ran on those requests, without a phrase to read.
        Assert.That(await IsRotationPendingAsync(recovered), Is.False);
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        var otherPhrase = BIP39Util.GenerateBIP39(ByteArrayUtil.GetRndByteArray(16));
        var mistyped = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(otherPhrase, keys.MasterKey));
        DriveAsserts.AssertRejected(mistyped, OdinClientErrorCode.RecoveryPhraseMismatch);

        var typed = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(phrase, keys.MasterKey));
        Assert.That(typed.StatusCode, Is.EqualTo(HttpStatusCode.OK), typed.Error?.Content);

        // Re-entry has no password to seal; the next password login writes the copy.
        var status = await ZkOwnerSteps.GetStatusAsync(recovered);
        Assert.That(status.RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver));
        Assert.That(status.HasPasswordCopy, Is.False);
        var written = await ZkOwnerSteps.SavePasswordAsync(recovered, NewPassword,
            ZkBrowser.SealPassword(NewPassword, status.PhrasePublicKeyJwk));
        Assert.That(written.StatusCode, Is.EqualTo(HttpStatusCode.OK), written.Error?.Content);

        var recovery = await Host.AnonymousRefitFor<IOwnerAuthenticationClient>(recovered.Identity.DomainName).GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(NewPassword));
    }

    /// <summary>
    /// A friend deal the server made before recovery in the browser locks the phrase and can't be dealt
    /// again (the browser deals friends now): it goes, and the security page says so.  The first owner
    /// request after the deploy removes it already; the takeover would too.
    /// </summary>
    [Test]
    public async Task AnOldServerMadeFriendDealIsRemovedAndTheSecurityPageSaysSo()
    {
        var (dealer, _) = Cast();
        await ArrangeAutomatedShardsAsync(dealer);

        // What an old friend deal looks like: the same package, not automated, locking the phrase
        var (scope, ctx) = await MigrationContextAsync(dealer);
        var shamir = scope.Resolve<ShamirConfigurationService>();
        var package = await shamir.GetDealerShardPackage(ctx);
        package.UsesAutomatedRecovery = false;
        await shamir.SaveDealerPackage(package, ctx);

        await TakeOverAsync(dealer);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(dealer)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver));
        await AssertDealGoneAndFlaggedAsync(dealer);
    }

    /// <summary>The login's takeover, from a fresh password login as the browser does it.</summary>
    private async Task<string> TakeOverAsync(OwnerSession dealer)
    {
        var fresh = await OwnerSession.LoginAsync(Host, dealer.Identity.DomainName);
        var keys = await ZkOwnerSteps.CreateKeysAsync(fresh, OwnerLogin.DefaultPassword);
        return await ZkOwnerSteps.TakeOverPhraseAsync(fresh, OwnerLogin.DefaultPassword, keys.MasterKey);
    }
}
