using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests._V2.Tests.Connections;

/// <summary>
/// SUPERSEDED — ported to
/// <c>tests/apps/Odin.Hosting.Tests.V2/Ported/Connections/PreflightIntroductionsTests.cs</c> on
/// 2026-05-13. All 6 cases are ported. The FirstRunInfo-clear case in the new port resolves the
/// tenant <c>IdentityDatabase</c> via <see cref="Hosting.OdinHost.GetTenantScope"/> instead of
/// reaching through <c>WebScaffold.Services</c>; the storage-context Guid + handle are reused
/// unchanged.
/// </summary>
public class V2PreflightIntroductionsTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
            TestIdentities.Merry,
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            await Cleanup();
        }
        catch
        {
            // best-effort
        }

        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task Preflight_WhenAllRecipientsConnected_ReturnsReady()
    {
        var introducer = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var merry = TestIdentities.Merry;

        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);

        await ConnectIntroducer(introducer, sam);
        await ConnectIntroducer(introducer, merry);

        var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.OdinId, merry.OdinId],
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK,
            $"expected 200 but got {response.StatusCode}");
        ClassicAssert.IsNotNull(response.Content);
        ClassicAssert.AreEqual(2, response.Content.Recipients.Count);

        AssertStatus(response.Content, sam.OdinId, IntroductionPreflightStatus.Ready);
        AssertStatus(response.Content, merry.OdinId, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content.Recipients.Single(r => r.Recipient == sam.OdinId.DomainName);
        ClassicAssert.IsTrue(samStatus.IsConfigured, "sam should report IsConfigured");
        ClassicAssert.IsFalse(samStatus.RequiresUpgrade, "sam should not require upgrade");
        ClassicAssert.IsTrue(samStatus.AllowsIntroductions, "sam should allow introductions from frodo");
    }

    [Test]
    public async Task Preflight_WhenRecipientNotConnected_ReturnsNotConnected()
    {
        var introducer = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var merry = TestIdentities.Merry;

        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);

        // Connect Frodo to Sam only — Merry remains unconnected.
        await ConnectIntroducer(introducer, sam);

        var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.OdinId, merry.OdinId],
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK);
        ClassicAssert.AreEqual(2, response.Content.Recipients.Count);

        AssertStatus(response.Content, sam.OdinId, IntroductionPreflightStatus.Ready);
        AssertStatus(response.Content, merry.OdinId, IntroductionPreflightStatus.NotConnected);

        var merryStatus = response.Content.Recipients.Single(r => r.Recipient == merry.OdinId.DomainName);
        ClassicAssert.IsNotNull(merryStatus.Detail, "NotConnected outcome should include a Detail");
    }

    [Test]
    public async Task Preflight_WhenRecipientRevokedAllowIntroductions_ReturnsIntroductionsNotPermitted()
    {
        var introducer = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var merry = TestIdentities.Merry;

        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);
        var samOwner = _scaffold.CreateOwnerApiClientRedux(sam);

        await ConnectIntroducer(introducer, sam);
        await ConnectIntroducer(introducer, merry);

        // Sam revokes the system circle that grants AllowIntroductions to Frodo. Frodo's ICR
        // with Sam remains, but introductions from Frodo would now be rejected at Sam.
        var revoke = await samOwner.Network.RevokeCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, introducer.OdinId);
        ClassicAssert.IsTrue(revoke.IsSuccessStatusCode,
            $"pre-seed: revoking ConfirmedConnections circle on sam failed: {revoke.StatusCode}");

        var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [sam.OdinId, merry.OdinId],
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK);

        AssertStatus(response.Content, sam.OdinId, IntroductionPreflightStatus.IntroductionsNotPermitted);
        AssertStatus(response.Content, merry.OdinId, IntroductionPreflightStatus.Ready);

        var samStatus = response.Content.Recipients.Single(r => r.Recipient == sam.OdinId.DomainName);
        ClassicAssert.IsTrue(samStatus.IsConfigured, "sam is still configured");
        ClassicAssert.IsFalse(samStatus.AllowsIntroductions,
            "sam should report AllowsIntroductions=false after revoking the circle");
    }

    [Test]
    public async Task Preflight_FiltersSelfFromRecipientList()
    {
        var introducer = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);

        await ConnectIntroducer(introducer, sam);

        var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [introducer.OdinId, sam.OdinId],
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK);
        ClassicAssert.AreEqual(1, response.Content.Recipients.Count,
            "self should be filtered from recipient list");
        AssertStatus(response.Content, sam.OdinId, IntroductionPreflightStatus.Ready);
    }

    [Test]
    public async Task Preflight_WhenRecipientNotConfigured_ReturnsRecipientNotConfigured()
    {
        // Connect Frodo to both Sam and Merry while everyone is still configured (the connection
        // handshake itself rejects unconfigured recipients — see CircleNetworkRequestService line
        // 504). Then surgically clear Merry's FirstRunInfo so IsIdentityServerConfiguredAsync flips
        // to false on her tenant. Frodo's existing ICR with Merry is unaffected, so the preflight
        // call still reaches Merry — and her PreflightIncomingIntroductionAsync should report
        // IsConfigured=false back to Frodo.

        var introducer = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var merry = TestIdentities.Merry;

        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);

        await ConnectIntroducer(introducer, sam);
        await ConnectIntroducer(introducer, merry);

        FirstRunInfo savedFirstRun = null;
        try
        {
            savedFirstRun = await ReadFirstRunInfoAsync(merry.OdinId);
            ClassicAssert.IsNotNull(savedFirstRun, "pre-seed: merry should have FirstRunInfo before we clear it");

            await ClearFirstRunInfoAsync(merry.OdinId);

            var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
            {
                Message = "preflight",
                Recipients = [sam.OdinId, merry.OdinId],
            });

            ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK,
                $"expected 200 but got {response.StatusCode}");
            ClassicAssert.AreEqual(2, response.Content.Recipients.Count);

            AssertStatus(response.Content, sam.OdinId, IntroductionPreflightStatus.Ready);
            AssertStatus(response.Content, merry.OdinId, IntroductionPreflightStatus.RecipientNotConfigured);

            var merryStatus = response.Content.Recipients.Single(r => r.Recipient == merry.OdinId.DomainName);
            ClassicAssert.IsFalse(merryStatus.IsConfigured, "merry should report IsConfigured=false");
            ClassicAssert.IsNotNull(merryStatus.Detail, "RecipientNotConfigured outcome should include a Detail");
        }
        finally
        {
            // Restore so subsequent tests in this fixture see merry configured again.
            if (savedFirstRun != null)
            {
                await UpsertFirstRunInfoAsync(merry.OdinId, savedFirstRun);
            }
        }
    }

    [Test]
    public async Task Preflight_WhenRecipientListEmpty_ReturnsBadRequest()
    {
        var introducer = TestIdentities.Frodo;
        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);

        var response = await CallPreflight(introducerOwner, introducer.OdinId, new IntroductionGroup
        {
            Message = "preflight",
            Recipients = new List<string>(),
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400 but got {response.StatusCode}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Refit.ApiResponse<IntroductionPreflightResult>> CallPreflight(
        OwnerApiClientRedux introducerOwner,
        OdinId introducerOdinId,
        IntroductionGroup group)
    {
        var callerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        await callerContext.Initialize(introducerOwner);
        var client = new V2IntroductionsClient(introducerOdinId, callerContext.GetFactory());
        return await client.PreflightIntroductionsAsync(group);
    }

    private async Task ConnectIntroducer(TestIdentity introducer, TestIdentity recipient)
    {
        var introducerOwner = _scaffold.CreateOwnerApiClientRedux(introducer);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var send = await introducerOwner.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
        ClassicAssert.IsTrue(send.IsSuccessStatusCode,
            $"ConnectIntroducer: send {introducer.OdinId} -> {recipient.OdinId} failed: {send.StatusCode}");

        var accept = await recipientOwner.Connections.AcceptConnectionRequest(introducer.OdinId);
        ClassicAssert.IsTrue(accept.IsSuccessStatusCode,
            $"ConnectIntroducer: accept on {recipient.OdinId} failed: {accept.StatusCode}");

        var icr = await introducerOwner.Network.GetConnectionInfo(recipient.OdinId);
        ClassicAssert.IsTrue(icr.IsSuccessStatusCode && icr.Content.Status == ConnectionStatus.Connected,
            $"ConnectIntroducer: introducer ICR with {recipient.OdinId} is {icr.Content?.Status}");
    }

    // Reproduces the private storage handle TenantConfigService uses internally so tests can
    // poke FirstRunInfo on a specific tenant. The context key here MUST match
    // TenantConfigService.ConfigContextKey — if that ever changes, this constant changes too.
    private static readonly SingleKeyValueStorage TestConfigStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("b9e1c2a3-e0e0-480e-a696-ce602b052d07"));

    private async Task<FirstRunInfo> ReadFirstRunInfoAsync(OdinId tenant)
    {
        var db = ResolveTenantIdentityDatabase(tenant);
        return await TestConfigStorage.GetAsync<FirstRunInfo>(db.KeyValueCached, FirstRunInfo.Key);
    }

    private async Task ClearFirstRunInfoAsync(OdinId tenant)
    {
        var db = ResolveTenantIdentityDatabase(tenant);
        await TestConfigStorage.DeleteAsync(db.KeyValueCached, FirstRunInfo.Key);
    }

    private async Task UpsertFirstRunInfoAsync(OdinId tenant, FirstRunInfo info)
    {
        var db = ResolveTenantIdentityDatabase(tenant);
        await TestConfigStorage.UpsertAsync(db.KeyValueCached, FirstRunInfo.Key, info);
    }

    private IdentityDatabase ResolveTenantIdentityDatabase(OdinId tenant)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        var scope = container.GetTenantScope(tenant.DomainName);
        return scope.Resolve<IdentityDatabase>();
    }

    private static void AssertStatus(IntroductionPreflightResult result, OdinId recipient, IntroductionPreflightStatus expected)
    {
        var match = result.Recipients.SingleOrDefault(r => r.Recipient == recipient.DomainName);
        ClassicAssert.IsNotNull(match, $"expected an entry for {recipient}");
        ClassicAssert.IsTrue(match.Status == expected,
            $"{recipient}: expected {expected} but got {match.Status} ({match.Detail})");
    }

    private async Task Cleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        // Disconnect any pairings.
        try { await frodo.Connections.DisconnectFrom(sam.Identity.OdinId); } catch { }
        try { await frodo.Connections.DisconnectFrom(merry.Identity.OdinId); } catch { }
        try { await sam.Connections.DisconnectFrom(frodo.Identity.OdinId); } catch { }
        try { await merry.Connections.DisconnectFrom(frodo.Identity.OdinId); } catch { }
        try { await sam.Connections.DisconnectFrom(merry.Identity.OdinId); } catch { }
        try { await merry.Connections.DisconnectFrom(sam.Identity.OdinId); } catch { }

        // Remove any leftover requests.
        try { await frodo.Connections.DeleteSentRequestTo(sam.Identity.OdinId); } catch { }
        try { await frodo.Connections.DeleteSentRequestTo(merry.Identity.OdinId); } catch { }
        try { await sam.Connections.DeleteSentRequestTo(frodo.Identity.OdinId); } catch { }
        try { await merry.Connections.DeleteSentRequestTo(frodo.Identity.OdinId); } catch { }

        try { await frodo.Connections.DeleteConnectionRequestFrom(sam.Identity.OdinId); } catch { }
        try { await frodo.Connections.DeleteConnectionRequestFrom(merry.Identity.OdinId); } catch { }
        try { await sam.Connections.DeleteConnectionRequestFrom(frodo.Identity.OdinId); } catch { }
        try { await merry.Connections.DeleteConnectionRequestFrom(frodo.Identity.OdinId); } catch { }
    }
}
