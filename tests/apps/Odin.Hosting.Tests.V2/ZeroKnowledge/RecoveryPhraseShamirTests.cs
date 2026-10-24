#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Server-dealt Shamir after the browser takes the recovery phrase over
/// (docs/zero-knowledge-recovery-plan.md, decision 6): it locks the master key, not the phrase, so it
/// keeps working once the server forgets the phrase.
/// </summary>
[TestFixture]
public class RecoveryPhraseShamirTests : ShamirFixture
{
    protected override string[] HostIdentities =>
        base.HostIdentities.Union(AutomatedPlayerIdentities).ToArray();

    [TestCase(PlayerType.Delegate)]
    [TestCase(PlayerType.Automatic)]
    public async Task ServerDealtRecoveryWorksAfterTheTakeoverAndThePhraseIsTypedInOnce(PlayerType playerType)
    {
        var (dealer, players) = Cast();
        var config = playerType == PlayerType.Delegate
            ? (await ArrangeDelegateShardsAsync()).Config
            : await ArrangeAutomatedShardsAsync(dealer);

        var phrase = await TakeOverAsync(dealer);

        var recovered = await RecoverAsync(dealer, players, config, NewPassword);

        // The reset forgot the zero-knowledge keys and every copy tied to them; the phrase is still
        // valid, and the owner types it in once against the new keys.
        var keys = await ZkOwnerSteps.CreateKeysAsync(recovered, NewPassword);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(recovered)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.NeedsReentry));

        // The re-deal after a password change ran on those requests, without a phrase to read.
        Assert.That(await IsRotationPendingAsync(recovered), Is.False);
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        var otherPhrase = BIP39Util.GenerateBIP39(ByteArrayUtil.GetRndByteArray(16));
        var mistyped = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(otherPhrase, keys.MasterKey, withCheck: true));
        DriveAsserts.AssertRejected(mistyped, OdinClientErrorCode.RecoveryPhraseMismatch);

        var typed = await ZkOwnerSteps.Client(recovered).ReenterPhrase(ZkBrowser.MakePhraseCopies(phrase, keys.MasterKey, withCheck: true));
        Assert.That(typed.StatusCode, Is.EqualTo(HttpStatusCode.OK), typed.Error?.Content);

        var recovery = await Host.AnonymousRefitFor<IZeroKnowledgeRecoveryClient>(recovered.Identity.DomainName).GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(NewPassword));
    }

    [Test]
    public async Task ADealThatLocksThePhraseIsDealtAgainAtTheTakeover()
    {
        var (dealer, players, before) = await ArrangeDelegateShardsAsync();
        await MakeTheDealLockThePhraseAsync(dealer);

        await TakeOverAsync(dealer);

        var after = await GetDealerShardConfigAsync(dealer);
        Assert.That(after.Updated.milliseconds, Is.GreaterThan(before.Updated.milliseconds));
        Assert.That(after.Envelopes.Select(e => e.Player.OdinId), Is.EquivalentTo(before.Envelopes.Select(e => e.Player.OdinId)));
        Assert.That(after.Envelopes.Select(e => e.ShardId), Has.None.AnyOf(before.Envelopes.Select(e => e.ShardId).ToArray()),
            "every shard is new");
        Assert.That(after.MinMatchingShards, Is.EqualTo(before.MinMatchingShards));
        await DrainAndVerifyShardsAsync(dealer, before.Envelopes.Count);

        await RecoverAsync(dealer, players, after, NewPassword);
    }

    /// <summary>The login's takeover, from a fresh password login as the browser does it.</summary>
    private async Task<string> TakeOverAsync(OwnerSession dealer)
    {
        var fresh = await OwnerSession.LoginAsync(Host, dealer.Identity.DomainName);
        var keys = await ZkOwnerSteps.CreateKeysAsync(fresh, OwnerLogin.DefaultPassword);
        return await ZkOwnerSteps.TakeOverPhraseAsync(fresh, keys.MasterKey);
    }

    /// <summary>
    /// Turns the dealer's key record into one from before recovery in the browser: the field the
    /// takeover looks at moves to the old one.  Its content never gets opened; the takeover deals again.
    /// </summary>
    private async Task MakeTheDealLockThePhraseAsync(OwnerSession dealer)
    {
        await using var scope = Host.GetTenantScope(dealer.Identity.DomainName).BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValueCached>();
        var id = ShamirConfigurationService.ShamirRecordStorageId;

        var record = await ShamirConfigurationService.Storage.GetAsync<ShamirKeyRecord>(tblKeyValue, id);
        Assert.That(record?.ShamirDistributionKeyEncryptedMasterKey, Is.Not.Null);
        record!.ShamirDistributionKeyEncryptedRecoveryKey = record.ShamirDistributionKeyEncryptedMasterKey;
        record.ShamirDistributionKeyEncryptedMasterKey = null;
        await ShamirConfigurationService.Storage.UpsertAsync(tblKeyValue, id, record);
    }

    private static async Task<DealerShardConfig> ArrangeAutomatedShardsAsync(OwnerSession dealer)
    {
        var enableResponse = await dealer.RefitFor<IRefitOwnerConfiguration>().EnableAutoPasswordRecovery();
        Assert.That(enableResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), enableResponse.Error?.Content);
        await dealer.Sync.DrainOutboxAsync();

        var config = await GetDealerShardConfigAsync(dealer);
        Assert.That(config.UsesAutomaticRecovery, Is.True);
        return config;
    }
}
