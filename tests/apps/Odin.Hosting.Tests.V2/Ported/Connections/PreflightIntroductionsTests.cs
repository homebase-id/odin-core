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
    public async Task Preflight_WhenRecipientRevokedAllowIntroductions_ReturnsIntroductionsNotPermitted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        // Sam revokes the system circle that grants AllowIntroductions to Frodo. Frodo's ICR with
        // Sam stays in place, but introductions from Frodo would be rejected at Sam's side.
        var revoke = await sam.Connections.RevokeCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, frodo.Identity);
        Assert.That(revoke.IsSuccessStatusCode, Is.True, $"revoke failed: {revoke.StatusCode}");

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
