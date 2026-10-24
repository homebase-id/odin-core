#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Base;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Recovery with friends, dealt and finished in the browser (docs/zero-knowledge-recovery-plan.md,
/// "Building the friends pair"): a gated friend's shard is sealed to their own browser, the friends seal
/// what they release to the recovering browser's throwaway key, and only that browser rebuilds the
/// secret and opens the password.  <see cref="ZkFriends"/> plays every browser.
/// </summary>
[TestFixture]
public class FriendRecoveryTests : ShamirFixture
{
    private const string Password = OwnerLogin.DefaultPassword;

    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
    [
        // a forged shard is refused with a security exception, which the server logs
        "did not hold shard"
    ];

    [Test]
    public async Task AMixedDealGivesThePasswordBackOnlyInTheRecoveringBrowser()
    {
        var (frodo, players) = Cast();
        var config = await ArrangeFriendDealAsync(frodo, MixedPlayers(players), minMatchingShards: 3);

        await EnterRecoveryModeAsync(frodo);

        // The two automatic friends released at once; that is never enough on its own
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingSufficientDelegateConfirmation);
        DriveAsserts.AssertRejected(await AnonymousSecurityOf(frodo).GetRecoveryBundle(), OdinClientErrorCode.RecoveryNotReady);

        var gated = players.Where(p => config.Envelopes.Single(e => e.Player.OdinId == p.Identity).Player.Type == PlayerType.Delegate).ToList();
        await ApproveEveryShardRequestAsync(gated.Take(1).ToList(), config);
        Assert.That(RecoveryLink(frodo, RecoveryNotificationKind.FriendsApproved).Link, Is.Null, "the email carries no link");

        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password));
        Assert.That(rebuilt.Bundle.Shards, Has.Count.EqualTo(3));

        // Another browser can't open what the server hands out
        var bundle = (await AnonymousSecurityOf(frodo).GetRecoveryBundle()).Content!;
        Assert.Throws(Is.InstanceOf<Exception>(), () => ZkFriends.Rebuild(bundle, ZkBrowser.MakeKeyPair()));

        // The password logs in as it always did, and the login ends recovery mode
        var recovered = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, rebuilt.Password!);
        await AssertRecoveryStateAsync(recovered, ShamirRecoveryState.None);

        // and the friends' secret opens the same zero-knowledge master key
        Assert.That(rebuilt.ZkMasterKey, Is.EqualTo(ZkMasterKeyOf(frodo)));
    }

    [TestCase(true, TestName = "AJunkPasswordCopyIsRepairedWithTheFriendsResetKey")]
    [TestCase(false, TestName = "AMissingPasswordCopyIsRepairedWithTheFriendsResetKey")]
    public async Task TheFriendsResetKeySetsANewPassword(bool junk)
    {
        var (frodo, players) = Cast();
        var config = await ArrangeFriendDealAsync(frodo, players.Select(p => (p, PlayerType.Delegate)).ToList(),
            minMatchingShards: 3, writePasswordCopy: false);

        if (junk)
        {
            // anyone with the password can write junk as the copy
            await WriteFriendsPasswordCopyAsync(frodo, Password);
            var status = await ZkOwnerSteps.GetStatusAsync(frodo);
            var written = await ZkOwnerSteps.Client(frodo).SavePassword(new Odin.Services.Security.ZeroKnowledge.ZkPasswordRequest
            {
                ShamirPublicKeyEncryptedPassword = ZkBrowser.SealPassword("not the password", status.ShamirPublicKeyJwk!),
                CurrentAuthenticationPasswordReply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
                    Host, frodo.Identity.DomainName, Password)
            });
            Assert.That(written.StatusCode, Is.EqualTo(HttpStatusCode.OK), written.Error?.Content);
        }

        await EnterRecoveryModeAsync(frodo);
        await ApproveEveryShardRequestAsync(players.Take(3).ToList(), config);
        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, junk ? Is.EqualTo("not the password") : Is.Null);

        var reset = await OwnerPasswordFlow.ResetPasswordWithResetKeyAsync(Host, frodo.Identity.DomainName,
            ZkFriends.ResetKey(rebuilt.Secret), NewPassword, ZkBrowser.MakeKeys(NewPassword, rebuilt.ZkMasterKey).Request);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), reset.Error?.Content);

        var recovered = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, NewPassword);
        Assert.That(ZkBrowser.OpenMasterKey(NewPassword, await ZkOwnerSteps.GetStatusAsync(recovered)), Is.EqualTo(ZkMasterKeyOf(frodo)),
            "the reset keeps the zero-knowledge master key");
        await AssertRecoveryStateAsync(recovered, ShamirRecoveryState.None);
    }

    [Test]
    public async Task ARecoveryStartedElsewhereCanNotBeEnteredFromTheOwnersBrowser()
    {
        var (frodo, players) = Cast();
        await ArrangeFriendDealAsync(frodo, MixedPlayers(players), minMatchingShards: 3);
        var security = AnonymousSecurityOf(frodo);

        // Someone starts recovery with a key of their own; the email reaches the owner
        var theirs = ZkBrowser.MakeKeyPair();
        var initiate = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = theirs.PublicKeyJwk });
        Assert.That(initiate.StatusCode, Is.EqualTo(HttpStatusCode.OK), initiate.Error?.Content);
        var nonceId = RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString();

        // The owner clicks it in their own browser
        var enter = await security.EnterRecoveryMode(new EnterRecoveryModeRequest
        {
            Id = nonceId,
            RecoveryPublicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk
        });
        DriveAsserts.AssertRejected(enter, OdinClientErrorCode.RecoveryNotReady);
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode);

        // A friend deal can't be started without the browser's key
        var noKey = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest());
        Assert.That(noKey.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), noKey.Error?.Content);
    }

    [Test]
    public async Task AFriendsResetLeavesTheirShardDeadAndTheDealerIsTold()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var friend = players[0];

        // A reset with today's master key gives the friend's drive new keys; the conversion deletes the
        // deposits sealed to the old write-only key instead of refusing
        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(friend, ZkMasterKeyOf(friend));
        var shardId = config.Envelopes.Single(e => e.Player.OdinId == friend.Identity).ShardId;
        var gone = await friend.Drives.Reader.GetFileHeaderByUniqueIdAsync(shardId, Odin.Services.Drives.WellKnownAppDrives.ShardRecoveryDrive.Alias);
        Assert.That(gone.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), "the dead shard is deleted");

        var results = (await SecurityOf(frodo).VerifyShards()).Content!.Players;
        Assert.That(results[friend.Identity.DomainName].IsValid, Is.False, "the dealer is told to deal again");
        foreach (var player in players.Skip(1))
        {
            Assert.That(results[player.Identity.DomainName].IsValid, Is.True, player.Identity.DomainName);
        }
    }

    [Test]
    public async Task OnlyTheShardsPlayerCanDeliverIt()
    {
        var (frodo, players) = Cast();
        var config = await ArrangeFriendDealAsync(frodo, MixedPlayers(players), minMatchingShards: 3);
        await EnterRecoveryModeAsync(frodo);

        // An automatic friend, a real player, hands in a gated friend's shard under the friend's name
        var automatic = players.First(p => config.Envelopes.Single(e => e.Player.OdinId == p.Identity).Player.Type == PlayerType.Automatic);
        var gatedEnvelope = config.Envelopes.First(e => e.Player.Type == PlayerType.Delegate);

        var factory = Host.GetTenantScope(automatic.Identity.DomainName).Resolve<IOdinHttpClientFactory>();
        var client = await factory.CreateClientAsync<IPeerPasswordRecoveryHttpClient>(frodo.Identity);
        var response = await client.SendPlayerShard(new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.Complete,
            Shard = new PlayerEncryptedShard
            {
                Id = gatedEnvelope.ShardId,
                Index = 1,
                Player = gatedEnvelope.Player,
                DealerEncryptedShard = ByteArrayUtil.GetRndByteArray(64)
            }
        });

        Assert.That(response.IsSuccessStatusCode, Is.False, $"a forged shard was accepted: {response.StatusCode}");
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingSufficientDelegateConfirmation);
    }

    [Test]
    public async Task APasswordChangeSealsTheNewPasswordForTheFriendsAndDealsNothingAgain()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();

        var status = await ZkOwnerSteps.GetStatusAsync(frodo);
        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, frodo.Identity.DomainName, Password, NewPassword);
        request.ZkKeys = ZkBrowser.MakeKeys(NewPassword, ZkMasterKeyOf(frodo), status.PhrasePublicKeyJwk, status.ShamirPublicKeyJwk).Request;
        var changed = await frodo.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(request);
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK), changed.Error?.Content);

        // Owner requests after the change: a friend deal is never dealt again (the secret didn't change)
        var owner = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, NewPassword);
        Assert.That((await owner.RefitFor<IRefitOwnerConfiguration>().GetTenantSettings()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 0);
        Assert.That(await IsRotationPendingAsync(owner), Is.False);
        Assert.That((await GetDealerShardConfigAsync(owner)).Updated.milliseconds, Is.EqualTo(config.Updated.milliseconds));

        var rebuilt = await RecoverInBrowserAsync(owner, players, config);
        Assert.That(rebuilt.Password, Is.EqualTo(NewPassword), "the friends give back the new password");
    }

    [Test]
    public async Task AFriendDealWhoseCopiesAreForgottenIsRemovedAndTheSecurityPageSaysSo()
    {
        var (frodo, _, _) = await ArrangeDelegateShardsAsync();
        Assert.That((await RecoveryInfoAsync(frodo)).IsConfigured, Is.True);

        // A reset with the phrase the server still holds (the old path) forgets the zero-knowledge keys,
        // and the friend deal's copies with them
        var phrase = (await ZkOwnerSteps.HandOverPhraseAsync(frodo, Password)).Content!.Phrase;
        var reset = await OwnerPasswordFlow.ResetPasswordUsingRecoveryKeyAsync(Host, frodo.Identity.DomainName, phrase, NewPassword);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), reset.Error?.Content);

        // The next owner request removes the deal: it could recover nothing
        var owner = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, NewPassword);
        Assert.That((await RecoveryInfoAsync(owner)).IsConfigured, Is.False, "the deal is gone");
        var needsAttention = await PlainSecurityOf(owner).GetNeedsAttention();
        Assert.That(needsAttention.StatusCode, Is.EqualTo(HttpStatusCode.OK), needsAttention.Error?.Content);
        Assert.That(needsAttention.Content!.NeedsAttention, Is.True, "the red dot on the security page");
    }
}
