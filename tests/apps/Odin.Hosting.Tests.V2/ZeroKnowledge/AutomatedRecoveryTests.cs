#nullable enable
using System;
using System.Collections.Generic;
using Autofac;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardCollection;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Core.Time;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Base;
using Odin.Services.Peer.Encryption;
using Odin.Services.Drives;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Ported.Shamir;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;

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

    /// <summary>
    /// A holder not reached when the recovery started over still holds the earlier start's key: its
    /// shard opens only in the earlier browser, so its link no longer works for this recovery.
    /// </summary>
    [Test]
    public async Task ALinkSealedToAnEarlierStartsKeyNoLongerWorks()
    {
        var (frodo, _) = Cast();
        await ArrangeAutomatedDealAsync(frodo);
        await EnterRecoveryModeAsync(frodo);
        var earlierKey = RecoveringBrowser!.PublicKeyJwk;
        var (holder, link) = ReleaseLinks(frodo).First();

        await ExitRecoveryModeAsync(frodo);
        await EnterRecoveryModeAsync(frodo);

        // As if the new start's ask never reached the holder: inside the email interval its request
        // keeps the code, and the key is the earlier one again
        var holderReleases = new AutomaticShardReleases(Host.GetTenantScope(holder.DomainName).Resolve<TableKeyValueCached>());
        Assert.That(await holderReleases.RequestAsync(Guid.Parse(link.ShardId), new OdinId(frodo.Identity.DomainName), earlierKey), Is.Null,
            "inside the interval the code stays");

        DriveAsserts.AssertRejected(await AnonymousSecurityOf(frodo).FillSlot(link), OdinClientErrorCode.ShardLinkNoLongerValid);
        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.AwaitingSufficientDelegateConfirmation);
    }

    /// <summary>
    /// The owner has one email, the security page's (#2016), and the deals follow it (#2017): every
    /// holder hashed the old address, so a change sends the verify link to the login, which deals again.
    /// The same address in another case is the same address.
    /// </summary>
    [Test]
    public async Task AnEmailChangeDealsAgainAndEveryRecoveryEmailGoesToTheOneAddress()
    {
        var (frodo, _) = Cast();
        await ArrangeAutomatedDealAsync(frodo);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.False);

        const string address = "Frodo.Baggins@Example.com";
        var changed = await ChangeRecoveryEmailAsync(frodo, address);
        Assert.That(RedirectOf(changed), Does.StartWith("/owner/login?redeal=1&returnUrl="), "the login deals again");
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.True, "every holder hashed the old address");

        await DealAutomatedRecoveryAsync(frodo);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.False);

        var lowerCase = address.ToLowerInvariant();
        var sameAgain = await ChangeRecoveryEmailAsync(frodo, lowerCase);
        Assert.That(RedirectOf(sameAgain), Is.EqualTo("/owner/security/overview?fv=1"), "the same address: nothing to deal");
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.False);

        // Every recovery email goes to it, and the holders take it
        await EnterRecoveryModeAsync(frodo);
        Assert.That(RecoveryLink(frodo, RecoveryNotificationKind.EnterRecoveryMode).To, Is.EqualTo(lowerCase));
        Assert.That((await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!.Email, Is.EqualTo(EmailMasker.Mask(lowerCase)));
        var holderEmails = RecoveryNotifications(frodo, RecoveryNotificationKind.ReleaseShard);
        Assert.That(holderEmails, Is.Not.Empty);
        Assert.That(holderEmails.Select(n => n.To), Is.All.EqualTo(lowerCase));

        await FillEverySlotAsync(frodo);
        Assert.That(RecoveryLink(frodo, RecoveryNotificationKind.ShardsCollected).To, Is.EqualTo(lowerCase));
        Assert.That((await RebuildInTheRecoveringBrowserAsync(frodo)).Password, Is.EqualTo(Password));
    }

    /// <summary>
    /// A deal from before deals recorded the email hash it was dealt with: nothing can tell whether the
    /// email changed since, so it isn't flagged, and the dealer sends the hash as such deals took it.
    /// </summary>
    [Test]
    public async Task ADealWithoutARecordedEmailHashIsNotFlaggedAndStillRecovers()
    {
        var (frodo, _) = Cast();
        await ArrangeAutomatedDealAsync(frodo);
        var (scope, context) = await MigrationContextAsync(frodo);
        var shamir = scope.Resolve<ShamirConfigurationService>();
        var package = (await shamir.GetDealerShardPackage(context))!;
        Assert.That(package.RecoveryEmailHash, Is.Not.Null, "a deal records it now");
        package.RecoveryEmailHash = null;
        await shamir.SaveDealerPackage(package, context);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.False);
        var rebuilt = await RecoverInBrowserAsync(frodo, [], await GetDealerShardConfigAsync(frodo));
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
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.True);

        var recovered = await RecoverServerMadeDealAsync(frodo, NewPassword);

        var status = await ZkOwnerSteps.GetStatusAsync(recovered);
        Assert.That(status.ZkSaltKek64, Is.Null, "a reset with today's master key forgets the zero-knowledge keys");
        Assert.That(status.DealNeedsDealing, Is.True, "the next password login deals it in the browser");
    }

    /// <summary>
    /// The apps' zero-knowledge keys were locked with the forgotten zero-knowledge master key, but the devices that hold
    /// one may be the only way left into its drives: the reset keeps every app's key and copy, and a device signed in
    /// before still reads Vault.
    /// </summary>
    [Test]
    public async Task AResetWithTodaysMasterKeyKeepsTheAppsKeysForTheirDevices()
    {
        var (frodo, _) = Cast();
        await MakeZeroKnowledgeAsync(frodo);
        var app = await AppSession.SetupAsync(frodo, ZkAppSteps.Vault, DrivePermission.Read);
        var converted = await ZkAppSteps.ConvertVaultAsync(frodo, ZkMasterKeyOf(frodo));
        var (device, appKey) = await ZkAppSteps.SignInAsync(frodo, app.AppId, ZkMasterKeyOf(frodo));
        var uniqueId = Guid.NewGuid();
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(ZkAppSteps.VaultId,
            ZkTestFiles.Metadata(uniqueId, "what's in the vault"), transitOptions: null!, keyHeader: fileKey,
            lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, converted.DriveKey));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var before = (await ZkAppSteps.GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);

        await ArrangeServerMadeAutomatedDealAsync(frodo);
        var recovered = await RecoverServerMadeDealAsync(frodo, NewPassword);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(recovered)).ZkSaltKek64, Is.Null, "the zero-knowledge master key is gone");

        var kept = (await ZkAppSteps.GetAppKeysAsync(recovered)).Single(a => a.AppId == app.AppId);
        Assert.That(kept.ZkMasterKeyEncryptedAppKey, Is.EqualTo(before.ZkMasterKeyEncryptedAppKey));
        Assert.That(kept.Drives.Single(d => d.DriveId == ZkAppSteps.VaultId).HasAppCopy, Is.True);

        var grant = await ZkAppSteps.GrantOnAsync(device, ZkAppSteps.Vault);
        Assert.That(grant.ZkAppKeyEncryptedStorageKey, Is.Not.Null, "the device still gets its copy of Vault's key");
        Assert.That(ZkBrowser.Open(grant.ZkAppKeyEncryptedStorageKey!, appKey), Is.EqualTo(converted.DriveKey));
        var seen = await device.Drives.Reader.GetBatchAsync(ZkAppSteps.VaultId, ZkTestFiles.QueryFor(uniqueId));
        Assert.That(seen.Content?.SearchResults, Has.Count.EqualTo(1), seen.Error?.Content);
    }

    [Test]
    public async Task AServerMadeDealIsDealtAgainAtTheNextPasswordLogin()
    {
        var (frodo, _) = Cast();
        await MakeZeroKnowledgeAsync(frodo);
        var before = await ArrangeServerMadeAutomatedDealAsync(frodo);

        // What the login does when the status asks for it
        var after = await ArrangeAutomatedDealAsync(frodo);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(frodo)).DealNeedsDealing, Is.False);
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
        var address = await recovery.GetRecoveryEmailAsync();
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

        var asSomeoneElse = await PeerRecoveryClientAsync(players[0].Identity, holder);
        var notTheDealer = await asSomeoneElse.RequestShard(Ask(address));
        Assert.That(notTheDealer.IsSuccessStatusCode, Is.False, $"someone else asked for the dealer's shard: {notTheDealer.StatusCode}");
        Assert.That(ReleaseLinks(frodo), Is.Empty, "no link went out");

        // The dealer's own request with the right address does email the owner, and while it is
        // pending nothing is released without the code that email carries
        var asked = await asDealer.RequestShard(Ask(address));
        Assert.That(asked.IsSuccessStatusCode, Is.True, asked.Error?.Content);
        Assert.That(ReleaseLinks(frodo).Select(l => l.Holder), Is.EqualTo(new[] { holder }));

        // Refused with the link's own code, which the dealer tells from a failure on the way
        var noCode = await asDealer.ReleaseAutomaticShard(new ReleaseAutomaticShardRequest { ShardId = envelope.ShardId, Code = new string('0', 32) });
        DriveAsserts.AssertRejected(noCode, OdinClientErrorCode.ShardLinkNoLongerValid);
        var link = ReleaseLinks(frodo).Single().Request;
        var codeButNotTheDealer = await asSomeoneElse.ReleaseAutomaticShard(new ReleaseAutomaticShardRequest
        {
            ShardId = envelope.ShardId,
            Code = link.Code
        });
        DriveAsserts.AssertRejected(codeButNotTheDealer, OdinClientErrorCode.ShardLinkNoLongerValid);
    }

    /// <summary>
    /// A recovery stored by the release before this one has no browser key (an automated one under
    /// way, or one waiting on its finalize link): it can't go on, so it reads as none, and a new start
    /// goes ahead.
    /// </summary>
    [Test]
    public async Task ARecoveryFromBeforeThisReleaseDoesNotBlockANewStart()
    {
        var (frodo, _) = Cast();
        var config = await ArrangeAutomatedDealAsync(frodo);
        var (scope, context) = await MigrationContextAsync(frodo);
        await ShamirRecoveryService.Storage.UpsertAsync(scope.Resolve<TableKeyValueCached>(), ShamirRecoveryService.ShamirStatusStorageId,
            new ShamirRecoveryStatusRecord { State = ShamirRecoveryState.AwaitingSufficientDelegateConfirmation, Updated = UnixTimeUtc.Now() });

        // and a shard it collected unsealed, as holders released them before
        var leftover = config.Envelopes.First();
        var collector = new PlayerShardCollector(scope.Resolve<StandardFileSystem>());
        await collector.SavePlayerShard(new PlayerEncryptedShard
        {
            Id = leftover.ShardId,
            Index = 1,
            Player = leftover.Player,
            DealerEncryptedShard = ByteArrayUtil.GetRndByteArray(48)
        }, context);
        Assert.That((await collector.GetCollectShards(context)).Select(c => c.Id), Is.EqualTo(new[] { leftover.ShardId }),
            "the leftover is where recovery counts shards");

        await AssertRecoveryStateAsync(frodo, ShamirRecoveryState.None);
        await EnterRecoveryModeAsync(frodo);
        Assert.That((await AnonymousSecurityOf(frodo).GetShamirRecoverStatus()).Content!.Filled, Is.EqualTo(0),
            "a fresh entry clears what an older recovery left");
        await FillEverySlotAsync(frodo);
        Assert.That((await RebuildInTheRecoveringBrowserAsync(frodo)).Password, Is.EqualTo(Password));
    }
}
