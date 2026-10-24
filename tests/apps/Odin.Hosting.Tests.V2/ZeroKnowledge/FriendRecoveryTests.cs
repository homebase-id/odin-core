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
        Assert.That((await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!.RecoveryKeyId,
            Is.EqualTo(Odin.Services.Security.ZeroKnowledge.ZkSeal.KeyId(RecoveringBrowser!.PublicKeyJwk)),
            "the status says which browser the recovery is for");
        DriveAsserts.AssertRejected(await AnonymousSecurityOf(frodo).GetRecoveryBundle(), OdinClientErrorCode.RecoveryNotReady);

        var gated = players.Where(p => config.Envelopes.Single(e => e.Player.OdinId == p.Identity).Player.Type == PlayerType.Delegate).ToList();
        var emailsBefore = RecoveryNotificationCount(frodo, RecoveryNotificationKind.FriendsApproved);
        await ApproveEveryShardRequestAsync(gated, config);
        Assert.That(RecoveryNotificationCount(frodo, RecoveryNotificationKind.FriendsApproved) - emailsBefore, Is.EqualTo(1),
            "the owner is told once, at the threshold");
        Assert.That(RecoveryLink(frodo, RecoveryNotificationKind.FriendsApproved).Link, Is.Null, "the email carries no link");

        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password));
        Assert.That(rebuilt.Bundle.Shards, Has.Count.EqualTo(4), "later approvals are collected too");

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
            await WriteFriendsPasswordCopyAsync(frodo, Password, copy: "not the password");
        }

        await EnterRecoveryModeAsync(frodo);
        await ApproveEveryShardRequestAsync(players.Take(3).ToList(), config);
        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, junk ? Is.EqualTo("not the password") : Is.Null);

        var reset = await OwnerPasswordFlow.ResetPasswordWithResetKeyAsync(Host, frodo.Identity.DomainName,
            ZkFriends.ResetKey(rebuilt.Secret), NewPassword, ZkBrowser.MakeKeys(NewPassword, rebuilt.ZkMasterKey).Request);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), reset.Error?.Content);
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.None);

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

        // The refusal didn't use the link up: the browser that started recovery can still open it
        var fromTheStartingBrowser = await security.EnterRecoveryMode(new EnterRecoveryModeRequest
        {
            Id = nonceId,
            RecoveryPublicKeyJwk = theirs.PublicKeyJwk
        });
        Assert.That(fromTheStartingBrowser.StatusCode, Is.EqualTo(HttpStatusCode.OK), fromTheStartingBrowser.Error?.Content);
        await ExitRecoveryModeAsync(frodo);

        // A friend deal can't be started without the browser's key
        var noKey = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest());
        Assert.That(noKey.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), noKey.Error?.Content);
    }

    [Test]
    public async Task AFriendsResetLeavesTheirShardDeadAndTheDealerIsTold()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var friend = players[0];
        await EnterRecoveryModeAsync(frodo);
        Assert.That(await GetPlayerShardRequestAsync(config, friend), Is.Not.Null);

        // A reset with today's master key gives the friend's drive new keys; the conversion deletes the
        // deposits sealed to the old write-only key instead of refusing, and the requests to release them
        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(friend, ZkMasterKeyOf(friend));
        Assert.That(await GetPlayerShardRequestAsync(config, friend), Is.Null, "a request for a dead shard goes with it");
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

        // The gated friend's own server, without them, hands in junk as their shard
        var gated = players.Single(p => p.Identity == gatedEnvelope.Player.OdinId);
        var gatedClient = await Host.GetTenantScope(gated.Identity.DomainName).Resolve<IOdinHttpClientFactory>()
            .CreateClientAsync<IPeerPasswordRecoveryHttpClient>(frodo.Identity);
        var junk = await gatedClient.SendPlayerShard(new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.Complete,
            Shard = new PlayerEncryptedShard
            {
                Id = gatedEnvelope.ShardId,
                Index = 1,
                Player = gatedEnvelope.Player,
                DealerEncryptedShard = ByteArrayUtil.GetRndByteArray(200)
            }
        });
        Assert.That(junk.IsSuccessStatusCode, Is.False, $"a shard not sealed to the recovering browser was accepted: {junk.StatusCode}");
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

        // Before any login removes it, the deal can't start a recovery that could give nothing back
        var initiate = await AnonymousSecurityOf(frodo).InitiateRecoveryMode(new InitiateRecoveryModeRequest
        {
            RecoveryPublicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk
        });
        DriveAsserts.AssertRejected(initiate, OdinClientErrorCode.PasswordRecoveryNotConfigured);

        // The next owner request removes the deal: it could recover nothing
        var owner = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, NewPassword);
        await AssertDealGoneAndFlaggedAsync(owner);
    }

    [Test]
    public async Task AnExitRequestDoesNotLoseTheRecoveringBrowsersKey()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        await EnterRecoveryModeAsync(frodo);

        // Anyone can ask to exit; collecting goes on until the owner confirms it by email
        var exit = await AnonymousSecurityOf(frodo).ExitRecoveryMode();
        Assert.That(exit.StatusCode, Is.EqualTo(HttpStatusCode.OK), exit.Error?.Content);

        await ApproveEveryShardRequestAsync(players.Take(config.MinMatchingShards).ToList(), config);
        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password));

        // An exit request once the friends approved doesn't take the bundle away either
        exit = await AnonymousSecurityOf(frodo).ExitRecoveryMode();
        Assert.That(exit.StatusCode, Is.EqualTo(HttpStatusCode.OK), exit.Error?.Content);
        Assert.That((await RebuildInTheRecoveringBrowserAsync(frodo)).Password, Is.EqualTo(Password));

        // and its email's link can't be used to enter recovery: it carries no browser key
        var exitNonce = RecoveryLink(frodo, RecoveryNotificationKind.ExitRecoveryMode).NonceId.ToString();
        var entered = await AnonymousSecurityOf(frodo).EnterRecoveryMode(new EnterRecoveryModeRequest
        {
            Id = exitNonce,
            RecoveryPublicKeyJwk = RecoveringBrowser!.PublicKeyJwk
        });
        DriveAsserts.AssertRejected(entered, OdinClientErrorCode.RecoveryNotReady);
    }

    [Test]
    public async Task ASecondEmailLinkDoesNotStartTheRecoveryOver()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var security = AnonymousSecurityOf(frodo);
        var browser = ZkBrowser.MakeKeyPair();

        // Started, then "send it again" before the first email was clicked: two links for one browser
        var links = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var initiate = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = browser.PublicKeyJwk });
            Assert.That(initiate.StatusCode, Is.EqualTo(HttpStatusCode.OK), initiate.Error?.Content);
            links.Add(RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString());
        }

        Assert.That(links[0], Is.Not.EqualTo(links[1]));
        var first = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = links[0], RecoveryPublicKeyJwk = browser.PublicKeyJwk });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), first.Error?.Content);
        RecoveringBrowser = browser;

        await ApproveEveryShardRequestAsync(players.Take(config.MinMatchingShards).ToList(), config);
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingRecoveringBrowser);

        // Back to the page through the other email
        var second = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = links[1], RecoveryPublicKeyJwk = browser.PublicKeyJwk });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK), second.Error?.Content);
        Assert.That((await RebuildInTheRecoveringBrowserAsync(frodo)).Password, Is.EqualTo(Password),
            "the collected shards and the state are as they were");
    }
    [Test]
    public async Task APasswordCopySealedToAnOldKeyIsRefused()
    {
        var (frodo, _, _) = await ArrangeDelegateShardsAsync();

        var stale = await ZkOwnerSteps.SavePasswordAsync(frodo, Password, phraseCopy: null,
            friendsCopy: ZkBrowser.SealPassword(Password, ZkBrowser.MakeKeyPair().PublicKeyJwk));
        DriveAsserts.AssertRejected(stale, OdinClientErrorCode.ArgumentError);
    }

    [Test]
    public async Task AnExitRequestChangesNothingUntilTheOwnerConfirmsIt()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        var security = AnonymousSecurityOf(frodo);
        var browser = ZkBrowser.MakeKeyPair();

        var initiate = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = browser.PublicKeyJwk });
        Assert.That(initiate.StatusCode, Is.EqualTo(HttpStatusCode.OK), initiate.Error?.Content);
        var link = RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString();

        // Someone asks to exit before the owner clicked the email: the link still works
        Assert.That((await security.ExitRecoveryMode()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode);

        var entered = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = link, RecoveryPublicKeyJwk = browser.PublicKeyJwk });
        Assert.That(entered.StatusCode, Is.EqualTo(HttpStatusCode.OK), entered.Error?.Content);
        RecoveringBrowser = browser;
        Assert.That(await GetPlayerShardRequestAsync(config, players[0]), Is.Not.Null, "the friends were asked");
    }

    [Test]
    public async Task AnotherBrowsersLinkCanNotJoinARecoveryUnderWay()
    {
        var (frodo, _, _) = await ArrangeDelegateShardsAsync();
        var security = AnonymousSecurityOf(frodo);
        var laptop = ZkBrowser.MakeKeyPair();
        var phone = ZkBrowser.MakeKeyPair();

        var links = new List<(ZkBrowser.KeyPair Browser, string Link)>();
        foreach (var browser in new[] { laptop, phone })
        {
            var initiate = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = browser.PublicKeyJwk });
            Assert.That(initiate.StatusCode, Is.EqualTo(HttpStatusCode.OK), initiate.Error?.Content);
            links.Add((browser, RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString()));
        }

        var fromLaptop = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = links[0].Link, RecoveryPublicKeyJwk = laptop.PublicKeyJwk });
        Assert.That(fromLaptop.StatusCode, Is.EqualTo(HttpStatusCode.OK), fromLaptop.Error?.Content);

        var fromPhone = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = links[1].Link, RecoveryPublicKeyJwk = phone.PublicKeyJwk });
        DriveAsserts.AssertRejected(fromPhone, OdinClientErrorCode.RecoveryUnderWayElsewhere);
        Assert.That((await security.GetShamirRecoverStatus()).Content!.RecoveryKeyId,
            Is.EqualTo(Odin.Services.Security.ZeroKnowledge.ZkSeal.KeyId(laptop.PublicKeyJwk)));
    }

    [Test]
    public async Task StartingOverWhileCollectingAsksTheFriendsAgain()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();
        await EnterRecoveryModeAsync(frodo);
        var approver = players[0];
        await ApproveEveryShardRequestAsync([approver], config);
        Assert.That(await GetPlayerShardRequestAsync(config, approver), Is.Null, "an approval takes the request away");

        // Under way, a start says nothing, even with a key that isn't one
        var security = AnonymousSecurityOf(frodo);
        var junk = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = "x" });
        Assert.That(junk.StatusCode, Is.EqualTo(HttpStatusCode.OK), junk.Error?.Content);

        // "Start over" from the same browser: a new link, and the friends are asked again
        var again = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest { RecoveryPublicKeyJwk = RecoveringBrowser!.PublicKeyJwk });
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), again.Error?.Content);
        var link = RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString();
        var entered = await security.EnterRecoveryMode(new EnterRecoveryModeRequest { Id = link, RecoveryPublicKeyJwk = RecoveringBrowser.PublicKeyJwk });
        Assert.That(entered.StatusCode, Is.EqualTo(HttpStatusCode.OK), entered.Error?.Content);

        var asked = await GetPlayerShardRequestAsync(config, approver);
        Assert.That(asked?.RecoveryPublicKeyJwk, Is.EqualTo(RecoveringBrowser.PublicKeyJwk), "asked again, for the same browser");
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingSufficientDelegateConfirmation);

        // and what was collected still counts
        await ApproveEveryShardRequestAsync(players.Skip(1).Take(config.MinMatchingShards - 1).ToList(), config);
        Assert.That((await RebuildInTheRecoveringBrowserAsync(frodo)).Password, Is.EqualTo(Password));
    }
}
