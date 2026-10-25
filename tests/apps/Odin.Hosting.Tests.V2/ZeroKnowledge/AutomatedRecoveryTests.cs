#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
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
        var links = ReleaseLinks(frodo, config);
        Assert.That(links.Select(l => l.Holder.DomainName), Is.EquivalentTo(AutomatedPlayerIdentities));
        var status = (await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!;
        Assert.That((status.Filled, status.Needed), Is.EqualTo((0, config.MinMatchingShards)));

        // The slots name the holders only to the browser that started
        var slots = OpenSlots(status.SealedSlots64);
        Assert.That(slots.Select(s => s.Player.OdinId.DomainName), Is.EquivalentTo(AutomatedPlayerIdentities));
        Assert.That(slots.Select(s => s.Filled), Is.All.False);
        Assert.Throws(Is.InstanceOf<Exception>(), () => ZkBrowser.Unseal(status.SealedSlots64, ZkBrowser.MakeKeyPair().PrivateKeyPkcs8));

        // One click per holder, from any device (the page posts no key), until the threshold
        var collectedEmails = RecoveryNotificationCount(frodo, RecoveryNotificationKind.ShardsCollected);
        for (var i = 0; i < config.MinMatchingShards; i++)
        {
            var filled = await AnonymousSecurityOf(frodo).FillSlot(links[i].Request);
            Assert.That(filled.StatusCode, Is.EqualTo(HttpStatusCode.OK), filled.Error?.Content);
        }

        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingRecoveringBrowser);
        Assert.That(RecoveryNotificationCount(frodo, RecoveryNotificationKind.ShardsCollected) - collectedEmails, Is.EqualTo(1),
            "the owner is told once, at the threshold");
        status = (await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!;
        Assert.That(OpenSlots(status.SealedSlots64).Count(s => s.Filled), Is.EqualTo(config.MinMatchingShards));

        var rebuilt = await RebuildInTheRecoveringBrowserAsync(frodo);
        Assert.That(rebuilt.Password, Is.EqualTo(Password), "the automated deal gives back the password");
        Assert.That(rebuilt.ZkMasterKey, Is.EqualTo(ZkMasterKeyOf(frodo)), "and so the zero-knowledge data");

        var recovered = await OwnerSession.LoginAsync(Host, frodo.Identity.DomainName, rebuilt.Password!);
        await AssertRecoveryStateAsync(recovered, ShamirRecoveryState.None);
    }

    [Test]
    public async Task AHolderLinkWorksOnceAndOnlyWithItsCode()
    {
        var (frodo, _) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);
        await EnterRecoveryModeAsync(frodo);
        var (_, link) = ReleaseLinks(frodo, config).First();
        var security = AnonymousSecurityOf(frodo);

        DriveAsserts.AssertRejected(await security.FillSlot(new FillSlotRequest { ShardId = link.ShardId, Code = new string('0', 32) }),
            OdinClientErrorCode.ShardLinkNoLongerValid);
        DriveAsserts.AssertRejected(await security.FillSlot(new FillSlotRequest { ShardId = Guid.NewGuid().ToString(), Code = link.Code }),
            OdinClientErrorCode.ShardLinkNoLongerValid);

        var filled = await security.FillSlot(link);
        Assert.That(filled.StatusCode, Is.EqualTo(HttpStatusCode.OK), filled.Error?.Content);
        DriveAsserts.AssertRejected(await security.FillSlot(link), OdinClientErrorCode.ShardLinkNoLongerValid);

        var status = (await security.GetShamirRecoverStatus()).Content!;
        Assert.That(status.Filled, Is.EqualTo(1), "a wrong code fills nothing, and a used one nothing more");
    }

    [Test]
    public async Task ALinkDoesNothingOnceRecoveryIsOverAndStartingAgainSoonReusesIt()
    {
        var (frodo, _) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);
        await EnterRecoveryModeAsync(frodo);
        var (holder, link) = ReleaseLinks(frodo, config).First();
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
        Assert.That(rebuilt.Bundle.IsServerMadeDeal, Is.False);
        Assert.That(rebuilt.Password, Is.EqualTo(Password));
        Assert.That(rebuilt.ZkMasterKey, Is.EqualTo(ZkMasterKeyOf(frodo)));
    }
}
