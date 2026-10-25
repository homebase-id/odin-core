#nullable enable
using System;
using System.Collections.Generic;
using Autofac;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Base;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The automated deal (docs/zero-knowledge-recovery-plan.md, "Building the automated deal"): dealt in
/// the browser to the host's automated identities, it gives back the password, and every automatic
/// holder releases its shard only through a link it emails the owner itself.  A server-made automated
/// deal, as every one was before, still recovers the account and is dealt again at the next password
/// login.
/// </summary>
[TestFixture]
public class AutomatedRecoveryTests : ShamirFixture
{
    private const string Password = OwnerLogin.DefaultPassword;

    protected override string[] HostIdentities =>
        base.HostIdentities.Union(AutomatedPlayerIdentities).ToArray();

    [Test]
    public async Task AnAutomatedDealGivesBackThePasswordWithOneClickPerHolder()
    {
        var (frodo, _) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);
        Assert.That(config.Envelopes.Select(e => e.Player.OdinId.DomainName), Is.EquivalentTo(AutomatedPlayerIdentities));
        Assert.That(config.Envelopes.Select(e => e.Player.Type), Is.All.EqualTo(PlayerType.Automatic));

        await EnterRecoveryModeAsync(frodo);

        // Every holder emailed the owner; nothing is released until a link is clicked
        var links = ReleaseLinks(frodo);
        Assert.That(links.Select(l => l.Holder.DomainName), Is.EquivalentTo(AutomatedPlayerIdentities));
        var status = (await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!;
        Assert.That((status.Filled, status.Needed), Is.EqualTo((0, config.MinMatchingShards)));

        // The slots name the holders only to the browser that started
        var slots = OpenSlots(status.SealedSlots64);
        Assert.That(slots.Select(s => s.Player.OdinId.DomainName), Is.EquivalentTo(AutomatedPlayerIdentities));
        Assert.That(slots.Select(s => s.Filled), Is.All.False);
        Assert.Throws(Is.InstanceOf<Exception>(), () => ZkBrowser.Unseal(status.SealedSlots64, ZkBrowser.MakeKeyPair().PrivateKeyPkcs8));

        // One click per holder, from any device (the page posts no key), until the threshold
        for (var i = 0; i < config.MinMatchingShards; i++)
        {
            var filled = await AnonymousSecurityOf(frodo).FillSlot(links[i].Request);
            Assert.That(filled.StatusCode, Is.EqualTo(HttpStatusCode.OK), filled.Error?.Content);
        }

        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingRecoveringBrowser);
        Assert.That(RecoveryNotificationCount(frodo, RecoveryNotificationKind.ShardsCollected), Is.EqualTo(1),
            "the owner is told once, at the threshold");
        status = (await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!;
        Assert.That(status.SealedSlots64, Is.Null, "the slots are shown only while shards are awaited");

        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password), "the automated deal gives back the password");
        Assert.That(rebuilt.ZkMasterKey, Is.EqualTo(ZkMasterKeyOf(frodo)), "and so the zero-knowledge data");

        var recovered = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, rebuilt.Password!);
        await AssertRecoveryStateAsync(recovered, ShamirRecoveryState.None);
    }

    /// <summary>
    /// A click doesn't use the link up: a failure on the dealer's side must not lose it, and a repeat
    /// fills nothing more.
    /// </summary>
    [Test]
    public async Task AHolderLinkWorksOnlyWithItsCodeAndARepeatFillsNothingMore()
    {
        var (frodo, _) = Cast();
        await ArrangeAutomatedDealAsync(frodo);
        await EnterRecoveryModeAsync(frodo);
        var (_, link) = ReleaseLinks(frodo).First();
        var security = AnonymousSecurityOf(frodo);

        DriveAsserts.AssertRejected(await security.FillSlot(new FillSlotRequest { ShardId = link.ShardId, Code = new string('0', 32) }),
            OdinClientErrorCode.ShardLinkNoLongerValid);
        DriveAsserts.AssertRejected(await security.FillSlot(new FillSlotRequest { ShardId = Guid.NewGuid().ToString(), Code = link.Code }),
            OdinClientErrorCode.ShardLinkNoLongerValid);

        DriveAsserts.AssertRejected(await security.FillSlot(new FillSlotRequest { ShardId = "not-a-shard", Code = link.Code }),
            OdinClientErrorCode.ShardLinkNoLongerValid);

        for (var click = 0; click < 2; click++)
        {
            var filled = await security.FillSlot(link);
            Assert.That(filled.StatusCode, Is.EqualTo(HttpStatusCode.OK), filled.Error?.Content);
        }

        var status = (await security.GetShamirRecoverStatus()).Content!;
        Assert.That(status.Filled, Is.EqualTo(1), "a wrong code fills nothing, and a repeat nothing more");
    }

    [Test]
    public async Task ALinkDoesNothingOnceRecoveryIsOverAndStartingAgainSoonReusesIt()
    {
        var (frodo, _) = Cast();
        await ArrangeAutomatedDealAsync(frodo);
        await EnterRecoveryModeAsync(frodo);
        var (holder, link) = ReleaseLinks(frodo).First();
        var emailed = RecoveryNotificationCount(frodo, RecoveryNotificationKind.ReleaseShard);

        await ExitRecoveryModeAsync(frodo);
        DriveAsserts.AssertRejected(await AnonymousSecurityOf(frodo).FillSlot(link), OdinClientErrorCode.RecoveryNotReady);

        // Within five minutes the holders send nothing new, and the first link, unused, still works
        await EnterRecoveryModeAsync(frodo);
        Assert.That(RecoveryNotificationCount(frodo, RecoveryNotificationKind.ReleaseShard), Is.EqualTo(emailed));
        var filled = await AnonymousSecurityOf(frodo).FillSlot(link);
        Assert.That(filled.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{holder}: {filled.Error?.Content}");

        // and its shard is sealed to the new recovering browser
        await FillEverySlotAsync(frodo);
        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password));
    }

    [Test]
    public async Task APasswordChangeSealsTheNewPasswordAndDealsNothingAgain()
    {
        var (frodo, _) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);

        var status = await ZkOwnerSteps.GetStatusAsync(frodo);
        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, frodo.Identity.DomainName, Password, NewPassword);
        request.ZkKeys = ZkBrowser.MakeKeys(NewPassword, ZkMasterKeyOf(frodo), status.PhrasePublicKeyJwk, status.ShamirPublicKeyJwk).Request;
        var changed = await SecurityOf(frodo).ResetPassword(request);
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK), changed.Error?.Content);

        var owner = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, NewPassword);
        Assert.That((await GetDealerShardConfigAsync(owner)).Updated.milliseconds, Is.EqualTo(config.Updated.milliseconds),
            "the deal is not dealt again");

        var rebuilt = await RecoverInBrowserAsync(owner, [], config);
        Assert.That(rebuilt.Password, Is.EqualTo(NewPassword));
    }

    /// <summary>
    /// Owners who forgot their password never log in again before recovering: a server-made deal must
    /// still get them back in.  Its secret opens only today's master key, so the zero-knowledge keys go.
    /// </summary>
    [Test]
    public async Task AServerMadeDealStillRecoversTheAccount()
    {
        var (frodo, _) = Cast();
        await MakeZeroKnowledgeAsync(frodo);
        await ArrangeServerMadeAutomatedDealAsync(frodo);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).AutomatedDealNeedsDealing, Is.True);

        var recovered = await RecoverServerMadeDealAsync(frodo, NewPassword);

        var status = await ZkOwnerSteps.GetStatusAsync(recovered);
        Assert.That(status.ZkSaltKek64, Is.Null, "a reset with today's master key forgets the zero-knowledge keys");
        Assert.That(status.AutomatedDealNeedsDealing, Is.True, "the next password login deals it in the browser");
    }

    [Test]
    public async Task AServerMadeDealIsDealtAgainAtTheNextPasswordLogin()
    {
        var (frodo, _) = Cast();
        await MakeZeroKnowledgeAsync(frodo);
        var before = await ArrangeServerMadeAutomatedDealAsync(frodo);

        // What the login does when the status asks for it
        var after = await ArrangeAutomatedDealAsync(frodo);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).AutomatedDealNeedsDealing, Is.False);
        Assert.That(after.Envelopes.Select(e => e.ShardId), Has.None.AnyOf(before.Envelopes.Select(e => e.ShardId).ToArray()),
            "every shard is new");

        var rebuilt = await RecoverInBrowserAsync(frodo, [], after);
        Assert.That(rebuilt.Bundle.Copies, Is.Not.Null, "dealt in the browser");
        Assert.That(rebuilt.Password, Is.EqualTo(Password));
        Assert.That(rebuilt.ZkMasterKey, Is.EqualTo(ZkMasterKeyOf(frodo)));
    }

    /// <summary>
    /// A friend deal the server made before recovery in the browser has no copies, and its gated
    /// friends can't release: it can give nothing back, so recovery says so at the start.
    /// </summary>
    [Test]
    public async Task AnOldServerMadeFriendDealIsRefusedAtTheStart()
    {
        var (frodo, _) = Cast();
        await ArrangeOldServerMadeFriendDealAsync(frodo);

        var started = await AnonymousSecurityOf(frodo).InitiateRecoveryMode(new InitiateRecoveryModeRequest
        {
            RecoveryPublicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk
        });
        DriveAsserts.AssertRejected(started, OdinClientErrorCode.PasswordRecoveryNotConfigured);
    }

    /// <summary>
    /// What someone holding the dealer's server, or a copy of its data and peer credentials, can get
    /// from a holder: a link sent to another address is refused, nothing is released without the code
    /// the holder emailed the owner, and an identity that isn't the dealer gets nothing either.
    /// </summary>
    [Test]
    public async Task AHolderEmailsOnlyTheHashedAddressAndReleasesOnlyWithItsCode()
    {
        var (frodo, players) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);
        var envelope = config.Envelopes.First();
        var holder = envelope.Player.OdinId;

        var (scope, _) = await MigrationContextAsync(frodo);
        var recovery = scope.Resolve<PasswordKeyRecoveryService>();
        var hash = await recovery.GetHashedRecoveryEmail();
        var address = (await recovery.GetRecoveryInfo())!.Email;
        var asDealer = await scope.Resolve<IOdinHttpClientFactory>().CreateClientAsync<IPeerPasswordRecoveryHttpClient>(holder);
        RetrieveShardRequest Ask(string email) => new()
        {
            ShardId = envelope.ShardId,
            HashedRecoveryEmail = hash,
            RecoveryPublicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk,
            RecoveryEmail = email
        };

        var redirected = await asDealer.RequestShard(Ask("someone-else@example.com"));
        Assert.That(redirected.IsSuccessStatusCode, Is.False, $"a link to another address was sent: {redirected.StatusCode}");
        Assert.That(ReleaseLinks(frodo), Is.Empty, "no link went out");

        var noCode = await asDealer.ReleaseAutomaticShard(new ReleaseAutomaticShardRequest { ShardId = envelope.ShardId, Code = new string('0', 32) });
        Assert.That(noCode.IsSuccessStatusCode, Is.False, $"a shard was released without the code: {noCode.StatusCode}");

        var asSomeoneElse = await Host.GetTenantScope(players[0].Identity.DomainName).Resolve<IOdinHttpClientFactory>()
            .CreateClientAsync<IPeerPasswordRecoveryHttpClient>(holder);
        var notTheDealer = await asSomeoneElse.RequestShard(Ask(address));
        Assert.That(notTheDealer.IsSuccessStatusCode, Is.False, $"someone else asked for the dealer's shard: {notTheDealer.StatusCode}");
        Assert.That(ReleaseLinks(frodo), Is.Empty, "no link went out");

        // The dealer's own request with the right address does email the owner
        var asked = await asDealer.RequestShard(Ask(address));
        Assert.That(asked.IsSuccessStatusCode, Is.True, asked.Error?.Content);
        Assert.That(ReleaseLinks(frodo).Select(l => l.Holder), Is.EqualTo(new[] { holder }));
    }
}
