using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_V2/Tests/Connections/V2PreflightIntroductionsTests</c>. Verifies the V2 preflight
/// endpoint that callers hit before <c>SendIntroductions</c>: returns per-recipient status (Ready
/// / NotConnected / IntroductionsNotPermitted / RecipientNotConfigured / self-filtered) plus
/// configuration flags. The unconfigured-recipient case pokes the recipient's tenant DB directly
/// to clear <see cref="FirstRunInfo"/>; uses <see cref="Hosting.OdinHost.GetTenantScope"/> as the
/// escape hatch.
/// </summary>
[TestFixture]
public class PreflightIntroductionsTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    // Mirrors the private storage handle that TenantConfigService uses internally so we can poke
    // FirstRunInfo on a specific tenant. The context key MUST match TenantConfigService's
    // ConfigContextKey — if that changes upstream, change it here too.
    private static readonly SingleKeyValueStorage TestConfigStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("b9e1c2a3-e0e0-480e-a696-ce602b052d07"));

    [Test]
    public async Task Preflight_WhenAllRecipientsConnected_ReturnsReady()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Recipients.Count, Is.EqualTo(2));
        AssertStatus(response.Content, sam.Identity, IntroductionPreflightStatus.Ready);
        AssertStatus(response.Content, merry.Identity, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.IsConfigured, Is.True);
        Assert.That(samStatus.RequiresUpgrade, Is.False);
        Assert.That(samStatus.AllowsIntroductions, Is.True);
        Assert.That(samStatus.IsCallerConnected, Is.True);
        Assert.That(samStatus.IsCallerConfirmed, Is.True);
        Assert.That(samStatus.CallerConnectionState, Is.EqualTo(PeerCallerConnectionState.Connected));
    }

    /// <summary>
    /// The headline case behind the "thomas does not allow introductions from you" report: an
    /// auto-connection that the recipient's owner has never confirmed. Both sides are healthy and the
    /// recipient decided nothing — AllowIntroductions is simply not carried by the Auto-connected circle,
    /// and confirming requires the recipient owner's master key. This must not report as
    /// <see cref="IntroductionPreflightStatus.IntroductionsNotPermitted"/>.
    ///
    /// <para>
    /// Sam's auto-accept is turned off <b>after</b> the auto-connect (auto-connect itself needs it on, or
    /// it returns PendingManualApproval instead of connecting). A recipient that still auto-accepts
    /// permits its auto-connections to introduce — see
    /// <see cref="Preflight_WhenRecipientAutoConnectedAndAutoAccepts_ReturnsReady"/>.
    /// </para>
    /// </summary>
    [Test]
    public async Task Preflight_WhenRecipientAutoConnectedButNotConfirmed_ReturnsRecipientConnectionNotConfirmed()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await AutoConnectAsync(frodo, sam);
        await DisableAutoAcceptAsync(sam);

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertStatus(response.Content!, sam.Identity, IntroductionPreflightStatus.RecipientConnectionNotConfirmed);

        var samStatus = response.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.IsConfigured, Is.True);
        Assert.That(samStatus.AllowsIntroductions, Is.False, "auto-connected circle carries no AllowIntroductions");
        Assert.That(samStatus.IsCallerConnected, Is.True, "the connection is healthy on both sides");
        Assert.That(samStatus.IsCallerConfirmed, Is.False);
        Assert.That(samStatus.CallerConnectionState, Is.EqualTo(PeerCallerConnectionState.Connected));
        Assert.That(samStatus.RemedyActor, Is.EqualTo(PreflightRemedyActor.Recipient));
        Assert.That(samStatus.IsTransient, Is.False);
    }

    /// <summary>
    /// The same unconfirmed auto-connection, but Sam still auto-accepts connection requests (the default).
    /// Having already decided to connect to whoever asks, Sam has nothing left to withhold from the
    /// identities that decision auto-connected, so Frodo may introduce without waiting on a confirm.
    /// </summary>
    [Test]
    public async Task Preflight_WhenRecipientAutoConnectedAndAutoAccepts_ReturnsReady()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await AutoConnectAsync(frodo, sam);

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertStatus(response.Content!, sam.Identity, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.AllowsIntroductions, Is.True);
        Assert.That(samStatus.IsCallerAutoConnected, Is.True);
        Assert.That(samStatus.IsCallerConfirmed, Is.False, "still nothing but an auto-connection");
    }

    [Test]
    public async Task Preflight_WhenRecipientConfirmsAutoConnection_FlipsToReady()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await AutoConnectAsync(frodo, sam);
        await DisableAutoAcceptAsync(sam);

        var before = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity]
        });
        AssertStatus(before.Content!, sam.Identity, IntroductionPreflightStatus.RecipientConnectionNotConfirmed);

        var confirm = await sam.Connections.ConfirmConnection(frodo.Identity);
        Assert.That(confirm.IsSuccessStatusCode, Is.True, $"confirm failed: {confirm.StatusCode}");

        var after = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity]
        });

        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertStatus(after.Content!, sam.Identity, IntroductionPreflightStatus.Ready);

        var samStatus = after.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.IsCallerConfirmed, Is.True);
        Assert.That(samStatus.AllowsIntroductions, Is.True);
    }

    /// <summary>
    /// One-sided ICR: the sender still holds a connected record but the recipient has no usable one, so
    /// the recipient's server falls back to its authenticated-but-unconnected context. Blocking is used to
    /// produce that state, which doubles as a check that a block is <b>not</b> disclosed to its target —
    /// it is deliberately reported as the generic "does not recognize the connection".
    /// </summary>
    [Test]
    public async Task Preflight_WhenRecipientDoesNotRecognizeConnection_DoesNotReportAsNotPermitted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await ConnectAsync(frodo, sam);

        var block = await sam.Connections.BlockConnection(frodo.Identity);
        Assert.That(block.IsSuccessStatusCode, Is.True, $"block failed: {block.StatusCode}");

        // Frodo's side is untouched, so he still believes he is connected.
        var frodoIcr = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(frodoIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var samStatus = response.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.Status, Is.EqualTo(IntroductionPreflightStatus.RecipientDoesNotRecognizeConnection),
            $"detail={samStatus.Detail}");
        Assert.That(samStatus.IsCallerConnected, Is.False);
        Assert.That(samStatus.IsCallerConfirmed, Is.False);
        Assert.That(samStatus.CallerConnectionState, Is.EqualTo(PeerCallerConnectionState.NotRecognized),
            "blocked must be indistinguishable from unknown");
        Assert.That(samStatus.RemedyActor, Is.EqualTo(PreflightRemedyActor.Caller));
    }

    [Test]
    public async Task Preflight_WhenRecipientNotConnected_ReturnsNotConnected()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await ConnectAsync(frodo, sam);
        // Merry intentionally unconnected.

        var merryId = new OdinId(Identities.Merry);
        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity, merryId]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Recipients.Count, Is.EqualTo(2));
        AssertStatus(response.Content, sam.Identity, IntroductionPreflightStatus.Ready);
        AssertStatus(response.Content, merryId, IntroductionPreflightStatus.NotConnected);

        var merryStatus = response.Content.Recipients.Single(r => r.Recipient == merryId.DomainName);
        Assert.That(merryStatus.Detail, Is.Not.Null);
    }

    [Test]
    public async Task Preflight_WhenRecipientRevokedTheCircleButKeptTheReview_StaysReady()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        // Sam revokes the system circle that used to be the only carrier of AllowIntroductions. It no
        // longer decides this: a reviewed caller may introduce, and revoking a circle does not un-review
        // anyone. Withdrawing introductions is un-reviewing them -- the test below.
        var revoke = await sam.Connections.RevokeCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, frodo.Identity);
        Assert.That(revoke.IsSuccessStatusCode, Is.True, $"revoke failed: {revoke.StatusCode}");

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertStatus(response.Content!, sam.Identity, IntroductionPreflightStatus.Ready);
        AssertStatus(response.Content!, merry.Identity, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.IsConfigured, Is.True);
        Assert.That(samStatus.AllowsIntroductions, Is.True, "the review, not the circle, is what permits");
        Assert.That(samStatus.IsCallerConnected, Is.True);
        Assert.That(samStatus.CallerConnectionState, Is.EqualTo(PeerCallerConnectionState.Connected));
    }

    /// <summary>
    /// Withdrawing introductions is un-reviewing the caller: it drops them off
    /// <see cref="SecurityGroupType.Reviewed"/>, which is what CallerMayIntroduce reads first.
    /// </summary>
    /// <remarks>
    /// Two steps today, one step later. The Confirmed Connections circle still carries an actual
    /// <see cref="PermissionKeys.AllowIntroductions"/> key, and CallerMayIntroduce still honours a held
    /// key, so un-reviewing alone leaves that key doing the permitting. When that circle retires -- it is
    /// the only thing granting the key, and an ambient circle cannot inherit it -- the revoke goes away
    /// and the un-review is the whole of it.
    /// </remarks>
    [Test]
    public async Task Preflight_WhenRecipientUnreviewsCaller_ReturnsIntroductionsNotPermitted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        var revoke = await sam.Connections.RevokeCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, frodo.Identity);
        Assert.That(revoke.IsSuccessStatusCode, Is.True, $"revoke failed: {revoke.StatusCode}");

        var unreview = await sam.Connections.UnreviewConnection(frodo.Identity);
        Assert.That(unreview.IsSuccessStatusCode, Is.True, $"unreview failed: {unreview.StatusCode}");

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertStatus(response.Content!, sam.Identity, IntroductionPreflightStatus.IntroductionsNotPermitted);
        AssertStatus(response.Content!, merry.Identity, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content!.Recipients.Single(r => r.Recipient == sam.Identity.DomainName);
        Assert.That(samStatus.IsConfigured, Is.True);
        Assert.That(samStatus.AllowsIntroductions, Is.False);

        // Still connected -- this is a withdrawn decision, not a lost connection.
        Assert.That(samStatus.IsCallerConnected, Is.True);
        Assert.That(samStatus.CallerConnectionState, Is.EqualTo(PeerCallerConnectionState.Connected));
    }

    [Test]
    public async Task Preflight_FiltersSelfFromRecipientList()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await ConnectAsync(frodo, sam);

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [frodo.Identity, sam.Identity]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Recipients.Count, Is.EqualTo(1), "self should be filtered out");
        AssertStatus(response.Content, sam.Identity, IntroductionPreflightStatus.Ready);
    }

    [Test]
    public async Task Preflight_WhenRecipientNotConfigured_ReturnsRecipientNotConfigured()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        // Surgically clear Merry's FirstRunInfo so IsIdentityServerConfiguredAsync flips to false.
        // Frodo's existing ICR with Merry is unaffected, so the preflight call still reaches Merry
        // and her PreflightIncomingIntroductionAsync reports IsConfigured=false.
        FirstRunInfo savedFirstRun = null!;
        try
        {
            savedFirstRun = await ReadFirstRunInfoAsync(Identities.Merry);
            Assert.That(savedFirstRun, Is.Not.Null);
            await ClearFirstRunInfoAsync(Identities.Merry);

            var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
            {
                Message = "preflight",
                Recipients = [sam.Identity, merry.Identity]
            });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content!.Recipients.Count, Is.EqualTo(2));
            AssertStatus(response.Content, sam.Identity, IntroductionPreflightStatus.Ready);
            AssertStatus(response.Content, merry.Identity, IntroductionPreflightStatus.RecipientNotConfigured);

            var merryStatus = response.Content.Recipients.Single(r => r.Recipient == merry.Identity.DomainName);
            Assert.That(merryStatus.IsConfigured, Is.False);
            Assert.That(merryStatus.Detail, Is.Not.Null);
        }
        finally
        {
            if (savedFirstRun != null)
            {
                await UpsertFirstRunInfoAsync(Identities.Merry, savedFirstRun);
            }
        }
    }

    [Test]
    public async Task Preflight_WhenRecipientListEmpty_ReturnsBadRequest()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var response = await frodo.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = new List<string>()
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Establishes an auto-connection: <c>auto-connect</c> sends with
    /// <see cref="ConnectionRequestOrigin.IdentityOwnerApp"/>, so the recipient grants the Auto-connected
    /// circle rather than Confirmed Connections. Nothing upgrades that automatically -- only the recipient
    /// owner's confirm, which needs the master key.
    /// </summary>
    private static async Task AutoConnectAsync(OwnerSession sender, OwnerSession recipient)
    {
        var response = await sender.Connections.AutoConnectAsync(new ConnectionRequestHeader
        {
            Id = Guid.NewGuid(),
            Recipient = recipient.Identity,
            Message = "auto",
            ContactData = new ContactRequestData(),
            CircleIds = []
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"auto-connect to {recipient.Identity} failed: {response.StatusCode}");
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected),
            $"auto-connect outcome: {response.Content.Outcome} / {response.Content.Detail}");

        var icr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(icr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
    }

    /// <summary>
    /// Turns off the recipient's auto-accept, which is what otherwise lets its auto-connections introduce
    /// without a confirm. Call it after <see cref="AutoConnectAsync"/> -- auto-connect needs the flag on.
    /// The fixture restores tenant settings between tests, so there is nothing to undo here.
    /// </summary>
    private static async Task DisableAutoAcceptAsync(OwnerSession recipient)
    {
        var flagSet = await recipient.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");
        Assert.That(flagSet.IsSuccessStatusCode, Is.True, $"flag update failed: {flagSet.StatusCode}");
    }

    private static async Task ConnectAsync(OwnerSession introducer, OwnerSession recipient)
    {
        var send = await introducer.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send to {recipient.Identity} failed: {send.StatusCode}");

        var accept = await recipient.Connections.AcceptConnectionRequest(introducer.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept on {recipient.Identity} failed: {accept.StatusCode}");

        var icr = await introducer.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(icr.IsSuccessStatusCode, Is.True);
        Assert.That(icr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected),
            $"introducer ICR with {recipient.Identity} is {icr.Content.Status}");
    }

    private static void AssertStatus(IntroductionPreflightResult result, OdinId recipient, IntroductionPreflightStatus expected)
    {
        var match = result.Recipients.SingleOrDefault(r => r.Recipient == recipient.DomainName);
        Assert.That(match, Is.Not.Null, $"expected an entry for {recipient}");
        Assert.That(match!.Status, Is.EqualTo(expected), $"{recipient}: detail={match.Detail}");
    }

    private async Task<FirstRunInfo> ReadFirstRunInfoAsync(string domain)
    {
        var db = Host.GetTenantScope(domain).Resolve<IdentityDatabase>();
        return await TestConfigStorage.GetAsync<FirstRunInfo>(db.KeyValueCached, FirstRunInfo.Key);
    }

    private async Task ClearFirstRunInfoAsync(string domain)
    {
        var db = Host.GetTenantScope(domain).Resolve<IdentityDatabase>();
        await TestConfigStorage.DeleteAsync(db.KeyValueCached, FirstRunInfo.Key);
    }

    private async Task UpsertFirstRunInfoAsync(string domain, FirstRunInfo info)
    {
        var db = Host.GetTenantScope(domain).Resolve<IdentityDatabase>();
        await TestConfigStorage.UpsertAsync(db.KeyValueCached, FirstRunInfo.Key, info);
    }
}
