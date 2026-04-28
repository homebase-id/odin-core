using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.Tests.Connections;

public class V2AutoConnectTests
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
            TestIdentities.Samwise
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
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable AllowedCallers()
    {
        yield return new object[]
        {
            new OwnerTestCase(TargetDrive.NewTargetDrive()),
            HttpStatusCode.OK
        };
        yield return new object[]
        {
            new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read,
                new TestPermissionKeyList(PermissionKeyAllowance.Apps.ToArray())),
            HttpStatusCode.OK
        };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenNoPriorState_EstablishesConnection(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
                $"expected {expectedStatusCode} but got {response.StatusCode}");
            AssertOutcome(response.Content, AutoConnectOutcome.Connected);
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenRecipientHasAutoAcceptDisabled_ReturnsPendingManualApproval(
        IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Disable auto-accept of connection requests on the recipient.
            var flagSet = await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");
            ClassicAssert.IsTrue(flagSet.IsSuccessStatusCode, "failed to set DisableAutoAcceptConnectionRequests");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.PendingManualApproval);

            // Neither side should be connected
            var senderIcr = await senderOwner.Network.GetConnectionInfo(recipient.OdinId);
            ClassicAssert.IsTrue(senderIcr.Content.Status != ConnectionStatus.Connected,
                $"sender should not be connected but was {senderIcr.Content.Status}");
            var recipientIcr = await recipientOwner.Network.GetConnectionInfo(sender.OdinId);
            ClassicAssert.IsTrue(recipientIcr.Content.Status != ConnectionStatus.Connected,
                $"recipient should not be connected but was {recipientIcr.Content.Status}");

            // Sender has a stored outgoing request; recipient has a stored pending incoming
            var sentResp = await senderOwner.Connections.GetOutgoingSentRequestTo(recipient.OdinId);
            ClassicAssert.IsTrue(sentResp.IsSuccessStatusCode && sentResp.Content != null,
                "sender should have an outgoing sent-request after PendingManualApproval");

            var pendingResp = await recipientOwner.Connections.GetIncomingRequestFrom(sender.OdinId);
            ClassicAssert.IsTrue(pendingResp.IsSuccessStatusCode && pendingResp.Content != null,
                "recipient should have an incoming pending request after PendingManualApproval");
        }
        finally
        {
            await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenIncomingRequestFromRecipientExists_ReturnsAcceptedFromExistingIncoming(
        IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Recipient sends the sender a connection request first (IdentityOwner origin — will
            // not auto-accept on sender, so it sits as a pending incoming).
            var preSend = await recipientOwner.Connections.SendConnectionRequest(sender.OdinId, new List<GuidId>());
            ClassicAssert.IsTrue(preSend.IsSuccessStatusCode, "pre-seed: recipient could not send connection request");

            // Confirm the sender has the incoming pending before invoking auto-connect
            var incoming = await senderOwner.Connections.GetIncomingRequestFrom(recipient.OdinId);
            ClassicAssert.IsTrue(incoming.IsSuccessStatusCode && incoming.Content != null,
                "pre-seed: sender should have a pending incoming request");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.AcceptedFromExistingIncoming);
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenAlreadyConnected_ReturnsAlreadyConnected(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Pre-seed full ICR via the normal owner flow
            await senderOwner.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            await recipientOwner.Connections.AcceptConnectionRequest(sender.OdinId);

            // Sanity: both sides connected
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.AlreadyConnected);

            // ICR state unchanged
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenOutgoingIdentityOwnerRequestExists_ReturnsOutgoingRequestAlreadyExists(
        IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Disable auto-accept on recipient so the seeded IdentityOwner request stays pending
            // (the recipient wouldn't auto-accept an IdentityOwner origin anyway, but this
            // guards against any future eligibility change).
            await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");

            // Sender seeds an outgoing IdentityOwner request (the owner flow).
            var preSend = await senderOwner.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            ClassicAssert.IsTrue(preSend.IsSuccessStatusCode, "pre-seed: sender could not send owner-origin request");

            // Confirm it's there
            var sent = await senderOwner.Connections.GetOutgoingSentRequestTo(recipient.OdinId);
            ClassicAssert.IsTrue(sent.IsSuccessStatusCode && sent.Content != null,
                "pre-seed: outgoing sent-request should exist");
            ClassicAssert.IsTrue(sent.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner,
                $"pre-seed: expected IdentityOwner origin but got {sent.Content.ConnectionRequestOrigin}");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.OutgoingRequestAlreadyExists);

            // Existing sent-request should still be there, and still be IdentityOwner origin
            var sentAfter = await senderOwner.Connections.GetOutgoingSentRequestTo(recipient.OdinId);
            ClassicAssert.IsTrue(sentAfter.IsSuccessStatusCode && sentAfter.Content != null,
                "existing outgoing request should not have been deleted");
            ClassicAssert.IsTrue(sentAfter.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner,
                "existing outgoing request origin should be unchanged");
        }
        finally
        {
            await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenRecipientIsSelf_ReturnsInvalidRequest(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise; // unused connection-wise; only for cleanup symmetry

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Pass sender's own OdinId as the recipient.
            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, sender.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.InvalidRequest);
            ClassicAssert.IsNotNull(response.Content.Detail, "self-recipient outcome should include a Detail message");
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_IgnoresCallerSuppliedOrigin(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Caller sets origin to IdentityOwner; the endpoint is expected to ignore it and
            // treat the request as IdentityOwnerApp regardless.
            var header = new ConnectionRequestHeader
            {
                Recipient = recipient.OdinId,
                Message = "origin override test",
                ContactData = new ContactRequestData { Name = "Frodo" },
                CircleIds = new List<GuidId>(),
                ConnectionRequestOrigin = ConnectionRequestOrigin.IdentityOwner,
            };

            var response = await CallAutoConnectWithHeader(callerContext, senderOwner, sender.OdinId, header);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.Connected);

            // The resulting ICR should record IdentityOwnerApp origin, not IdentityOwner.
            var senderIcr = await senderOwner.Network.GetConnectionInfo(recipient.OdinId);
            ClassicAssert.IsTrue(senderIcr.IsSuccessStatusCode);
            ClassicAssert.IsTrue(
                senderIcr.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwnerApp,
                $"expected IdentityOwnerApp but ICR origin was {senderIcr.Content.ConnectionRequestOrigin}");
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenSenderHasBlockedRecipient_ReturnsBlocked(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Sender blocks the recipient before attempting auto-connect.
            var blockResp = await senderOwner.Network.BlockConnection(recipient.OdinId);
            ClassicAssert.IsTrue(blockResp.IsSuccessStatusCode,
                $"pre-seed: block failed with {blockResp.StatusCode}");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.Blocked);

            // Sender's record should still be Blocked (not mutated by the call).
            var senderIcr = await senderOwner.Network.GetConnectionInfo(recipient.OdinId);
            ClassicAssert.IsTrue(senderIcr.Content.Status == ConnectionStatus.Blocked,
                $"expected sender ICR to remain Blocked, was {senderIcr.Content.Status}");
        }
        finally
        {
            try { await senderOwner.Network.UnblockConnection(recipient.OdinId); } catch { }
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenSenderIcrIsStaleAndRecipientHasNoIcr_DoesNotShortCircuitToAlreadyConnected(
        IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Regression: the pre-ICR check used to short-circuit to AlreadyConnected based purely
        // on the sender's local IsConnected(). If the recipient had wiped their ICR (one-sided
        // disconnect), the sender would falsely report AlreadyConnected even though the recipient
        // no longer has a corresponding connection. The fix verifies with the recipient before
        // short-circuiting; on verification failure, the request flows through and re-establishes.

        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Establish a real bilateral connection.
            await senderOwner.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            await recipientOwner.Connections.AcceptConnectionRequest(sender.OdinId);
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);

            // One-sided disconnect: recipient wipes their ICR for the sender. Sender's ICR for
            // the recipient remains in Connected state — the stale half of the bug scenario.
            var disc = await recipientOwner.Connections.DisconnectFrom(sender.OdinId);
            ClassicAssert.IsTrue(disc.IsSuccessStatusCode, "pre-seed: one-sided disconnect failed");

            var senderIcrBefore = await senderOwner.Network.GetConnectionInfo(recipient.OdinId);
            ClassicAssert.IsTrue(senderIcrBefore.Content.Status == ConnectionStatus.Connected,
                "pre-seed: sender ICR should still be Connected after recipient-only disconnect");
            var recipientIcrBefore = await recipientOwner.Network.GetConnectionInfo(sender.OdinId);
            ClassicAssert.IsTrue(recipientIcrBefore.Content.Status != ConnectionStatus.Connected,
                "pre-seed: recipient ICR should not be Connected after disconnect");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            // Regression assertion: the pre-ICR check must NOT short-circuit to AlreadyConnected
            // when the recipient has no corresponding ICR. The exact downstream outcome
            // (Connected vs PendingManualApproval) depends on whether auto-accept on the recipient
            // completes the handshake under one-sided-disconnect leftover state — that's not what
            // we're pinning here.
            ClassicAssert.IsTrue(response.Content.Outcome != AutoConnectOutcome.AlreadyConnected,
                $"pre-ICR check short-circuited to AlreadyConnected despite stale ICR; got {response.Content.Outcome}");
        }
        finally
        {
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    [Test]
    [TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenLocalConnectedButRecipientCannotAccept_ReturnsPendingManualApproval(
        IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Regression: post-send classification used to report Connected/AcceptedFromExistingIncoming
        // based on the sender's local IsConnected() alone. If the sender's ICR was stale-Connected
        // and the recipient declined to auto-accept the new request, the sender would falsely
        // claim success when in fact the recipient only had a pending-incoming. The fix verifies
        // with the recipient post-send and falls back to PendingManualApproval on failure.

        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        try
        {
            // Establish a real bilateral connection, then one-sided disconnect on recipient.
            await senderOwner.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            await recipientOwner.Connections.AcceptConnectionRequest(sender.OdinId);
            await AssertBothSidesConnected(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);

            var disc = await recipientOwner.Connections.DisconnectFrom(sender.OdinId);
            ClassicAssert.IsTrue(disc.IsSuccessStatusCode, "pre-seed: one-sided disconnect failed");

            // Disable auto-accept on recipient so the new request from the auto-connect call
            // sits as a pending incoming rather than completing the handshake.
            var flagSet = await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");
            ClassicAssert.IsTrue(flagSet.IsSuccessStatusCode, "failed to set DisableAutoAcceptConnectionRequests");

            var senderIcrBefore = await senderOwner.Network.GetConnectionInfo(recipient.OdinId);
            ClassicAssert.IsTrue(senderIcrBefore.Content.Status == ConnectionStatus.Connected,
                "pre-seed: sender ICR should still be Connected (stale)");

            var response = await CallAutoConnect(callerContext, senderOwner, sender.OdinId, recipient.OdinId);

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);
            AssertOutcome(response.Content, AutoConnectOutcome.PendingManualApproval);

            // Recipient should have received the new request as a pending incoming.
            var pendingResp = await recipientOwner.Connections.GetIncomingRequestFrom(sender.OdinId);
            ClassicAssert.IsTrue(pendingResp.IsSuccessStatusCode && pendingResp.Content != null,
                "recipient should have a pending incoming request after PendingManualApproval");
        }
        finally
        {
            await recipientOwner.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
            await FullCleanup(senderOwner, recipientOwner, sender.OdinId, recipient.OdinId);
            await callerContext.Cleanup();
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<ApiResponse<ConnectionRequestResult>> CallAutoConnect(
        IApiClientContext callerContext,
        OwnerApiClientRedux senderOwner,
        OdinId senderOdinId,
        OdinId recipientOdinId)
    {
        var header = new ConnectionRequestHeader
        {
            Recipient = recipientOdinId,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>(),
        };
        return await CallAutoConnectWithHeader(callerContext, senderOwner, senderOdinId, header);
    }

    private static async Task<ApiResponse<ConnectionRequestResult>> CallAutoConnectWithHeader(
        IApiClientContext callerContext,
        OwnerApiClientRedux senderOwner,
        OdinId senderOdinId,
        ConnectionRequestHeader header)
    {
        // Make the drive used by the app-auth flow exist (owner flow doesn't need it, but
        // AppTestCase.Initialize does). Ignore conflicts on re-run.
        try
        {
            await senderOwner.DriveManager.CreateDrive(callerContext.TargetDrive, "auto-connect test drive", "",
                allowAnonymousReads: false);
        }
        catch
        {
            // drive may already exist from a previous test-case-source use of the same target
        }

        await callerContext.Initialize(senderOwner);
        var client = new V2ConnectionRequestsClient(senderOdinId, callerContext.GetFactory());
        return await client.AutoConnectAsync(header);
    }

    private static void AssertOutcome(ConnectionRequestResult result, AutoConnectOutcome expected)
    {
        ClassicAssert.IsNotNull(result, "result body was null");
        ClassicAssert.IsTrue(result.Outcome == expected,
            $"expected {expected} but got {result.Outcome} ({result.Detail})");
    }

    private static async Task AssertBothSidesConnected(
        OwnerApiClientRedux senderOwner, OwnerApiClientRedux recipientOwner,
        OdinId senderOdinId, OdinId recipientOdinId)
    {
        var senderIcr = await senderOwner.Network.GetConnectionInfo(recipientOdinId);
        ClassicAssert.IsTrue(senderIcr.IsSuccessStatusCode);
        ClassicAssert.IsTrue(senderIcr.Content.Status == ConnectionStatus.Connected,
            $"sender ICR status was {senderIcr.Content.Status}");

        var recipientIcr = await recipientOwner.Network.GetConnectionInfo(senderOdinId);
        ClassicAssert.IsTrue(recipientIcr.IsSuccessStatusCode);
        ClassicAssert.IsTrue(recipientIcr.Content.Status == ConnectionStatus.Connected,
            $"recipient ICR status was {recipientIcr.Content.Status}");
    }

    private static async Task FullCleanup(
        OwnerApiClientRedux senderOwner,
        OwnerApiClientRedux recipientOwner,
        OdinId senderOdinId,
        OdinId recipientOdinId)
    {
        // Disconnect any established ICRs
        try { await senderOwner.Connections.DisconnectFrom(recipientOdinId); } catch { }
        try { await recipientOwner.Connections.DisconnectFrom(senderOdinId); } catch { }

        // Remove any pending or sent requests either side may have left behind
        try { await senderOwner.Connections.DeleteSentRequestTo(recipientOdinId); } catch { }
        try { await recipientOwner.Connections.DeleteSentRequestTo(senderOdinId); } catch { }
        try { await senderOwner.Connections.DeleteConnectionRequestFrom(recipientOdinId); } catch { }
        try { await recipientOwner.Connections.DeleteConnectionRequestFrom(senderOdinId); } catch { }
    }
}
