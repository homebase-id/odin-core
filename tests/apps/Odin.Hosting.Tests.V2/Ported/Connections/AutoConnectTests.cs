using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_V2/Tests/Connections/V2AutoConnectTests</c>. Each scenario runs against the Owner
/// caller and the App caller (an app registered on the sender with the
/// <see cref="PermissionKeyAllowance.Apps"/> key set), exercising the V2 auto-connect endpoint's
/// outcome matrix: no-prior-state, recipient-disabled-auto-accept (PendingManualApproval),
/// existing-incoming-from-recipient (AcceptedFromExistingIncoming), already-connected, existing
/// outgoing IdentityOwner request, self-recipient (InvalidRequest), caller-supplied origin is
/// ignored, sender-blocks (Blocked), stale-sender-ICR doesn't short-circuit to AlreadyConnected,
/// and stale-but-recipient-cannot-accept yields PendingManualApproval.
/// </summary>
[TestFixture]
public class AutoConnectTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    public enum CallerKind
    {
        Owner,
        App
    }

    /// <summary>
    /// After the per-test DB restore, the in-memory <c>TenantContext.Settings</c> on each tenant
    /// is NOT re-read from disk — tests that set <c>DisableAutoAcceptConnectionRequests=true</c>
    /// leak that setting to the next test. Force it back to "false" on every identity at the start
    /// of each test so the default state is consistent regardless of test order.
    /// </summary>
    [SetUp]
    public async Task ResetAutoAcceptFlags()
    {
        if (Host == null) return;
        foreach (var domain in Host.Identities)
        {
            var owner = await LoginAsOwner(domain);
            await owner.Admin.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
        }
    }

    public static IEnumerable<object[]> AllowedCallers()
    {
        yield return [CallerKind.Owner];
        yield return [CallerKind.App];
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenNoPriorState_EstablishesConnection(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");
        AssertOutcome(response.Content!, AutoConnectOutcome.Connected);
        await AssertBothSidesConnected(sender, recipient);
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenRecipientHasAutoAcceptDisabled_ReturnsPendingManualApproval(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var flagSet = await recipient.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");
        Assert.That(flagSet.IsSuccessStatusCode, Is.True);

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.PendingManualApproval);

        var senderIcr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcr.Content!.Status, Is.Not.EqualTo(ConnectionStatus.Connected));
        var recipientIcr = await recipient.Connections.GetConnectionInfo(sender.Identity);
        Assert.That(recipientIcr.Content!.Status, Is.Not.EqualTo(ConnectionStatus.Connected));

        var sentResp = await sender.Connections.GetOutgoingSentRequestTo(recipient.Identity);
        Assert.That(sentResp.IsSuccessStatusCode, Is.True);
        Assert.That(sentResp.Content, Is.Not.Null);

        var pendingResp = await recipient.Connections.GetIncomingRequestFrom(sender.Identity);
        Assert.That(pendingResp.IsSuccessStatusCode, Is.True);
        Assert.That(pendingResp.Content, Is.Not.Null);
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenIncomingRequestFromRecipientExists_ReturnsAcceptedFromExistingIncoming(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        // Recipient sends sender a connection request first — IdentityOwner origin so sender won't
        // auto-accept; it sits as a pending incoming on sender.
        var preSend = await recipient.Connections.SendConnectionRequest(sender.Identity);
        Assert.That(preSend.IsSuccessStatusCode, Is.True);

        var incoming = await sender.Connections.GetIncomingRequestFrom(recipient.Identity);
        Assert.That(incoming.IsSuccessStatusCode, Is.True);
        Assert.That(incoming.Content, Is.Not.Null);

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.AcceptedFromExistingIncoming);
        await AssertBothSidesConnected(sender, recipient);
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenAlreadyConnected_ReturnsAlreadyConnected(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        await sender.Connections.SendConnectionRequest(recipient.Identity);
        await recipient.Connections.AcceptConnectionRequest(sender.Identity);
        await AssertBothSidesConnected(sender, recipient);

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.AlreadyConnected);

        await AssertBothSidesConnected(sender, recipient);
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenOutgoingIdentityOwnerRequestExists_ReturnsOutgoingRequestAlreadyExists(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        await recipient.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");

        var preSend = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(preSend.IsSuccessStatusCode, Is.True);

        var sent = await sender.Connections.GetOutgoingSentRequestTo(recipient.Identity);
        Assert.That(sent.IsSuccessStatusCode, Is.True);
        Assert.That(sent.Content, Is.Not.Null);
        Assert.That(sent.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.IdentityOwner));

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.OutgoingRequestAlreadyExists);

        var sentAfter = await sender.Connections.GetOutgoingSentRequestTo(recipient.Identity);
        Assert.That(sentAfter.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.IdentityOwner));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenRecipientIsSelf_ReturnsInvalidRequest(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        // Recipient not needed except to keep HostIdentities consistent; pass sender's own id.

        var response = await CallAutoConnectAsync(sender, kind, sender.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.InvalidRequest);
        Assert.That(response.Content!.Detail, Is.Not.Null,
            "self-recipient outcome should include a Detail message");
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_IgnoresCallerSuppliedOrigin(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var header = new ConnectionRequestHeader
        {
            Recipient = recipient.Identity,
            Message = "origin override test",
            ContactData = new ContactRequestData { Name = "Frodo" },
            CircleIds = new List<GuidId>(),
            ConnectionRequestOrigin = ConnectionRequestOrigin.IdentityOwner
        };

        var response = await CallAutoConnectWithHeaderAsync(sender, kind, header);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.Connected);

        var senderIcr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcr.IsSuccessStatusCode, Is.True);
        Assert.That(senderIcr.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.IdentityOwnerApp),
            $"expected IdentityOwnerApp but ICR origin was {senderIcr.Content.ConnectionRequestOrigin}");
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenSenderHasBlockedRecipient_ReturnsBlocked(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var blockResp = await sender.Connections.BlockConnection(recipient.Identity);
        Assert.That(blockResp.IsSuccessStatusCode, Is.True);

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.Blocked);

        var senderIcr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Blocked));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenSenderIcrIsStaleAndRecipientHasNoIcr_DoesNotShortCircuitToAlreadyConnected(CallerKind kind)
    {
        // Regression: the pre-ICR check used to short-circuit to AlreadyConnected based purely on
        // the sender's local IsConnected(). After a one-sided disconnect (recipient wipes their
        // ICR), the sender's ICR is stale-Connected. The fix verifies with the recipient before
        // short-circuiting and re-runs the request flow.
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        await sender.Connections.SendConnectionRequest(recipient.Identity);
        await recipient.Connections.AcceptConnectionRequest(sender.Identity);
        await AssertBothSidesConnected(sender, recipient);

        var disc = await recipient.Connections.DisconnectFrom(sender.Identity);
        Assert.That(disc.IsSuccessStatusCode, Is.True);

        var senderIcrBefore = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcrBefore.Content!.Status, Is.EqualTo(ConnectionStatus.Connected),
            "pre-seed: sender ICR should still be Connected after recipient-only disconnect");
        var recipientIcrBefore = await recipient.Connections.GetConnectionInfo(sender.Identity);
        Assert.That(recipientIcrBefore.Content!.Status, Is.Not.EqualTo(ConnectionStatus.Connected));

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Outcome, Is.Not.EqualTo(AutoConnectOutcome.AlreadyConnected),
            $"pre-ICR check short-circuited despite stale ICR; got {response.Content.Outcome}");
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task AutoConnect_WhenLocalConnectedButRecipientCannotAccept_ReturnsPendingManualApproval(CallerKind kind)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        await sender.Connections.SendConnectionRequest(recipient.Identity);
        await recipient.Connections.AcceptConnectionRequest(sender.Identity);
        await AssertBothSidesConnected(sender, recipient);

        var disc = await recipient.Connections.DisconnectFrom(sender.Identity);
        Assert.That(disc.IsSuccessStatusCode, Is.True);

        await recipient.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "true");

        var senderIcrBefore = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcrBefore.Content!.Status, Is.EqualTo(ConnectionStatus.Connected),
            "pre-seed: sender ICR should still be Connected (stale)");

        var response = await CallAutoConnectAsync(sender, kind, recipient.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOutcome(response.Content!, AutoConnectOutcome.PendingManualApproval);

        var pendingResp = await recipient.Connections.GetIncomingRequestFrom(sender.Identity);
        Assert.That(pendingResp.IsSuccessStatusCode, Is.True);
        Assert.That(pendingResp.Content, Is.Not.Null);
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private static async Task<ApiResponse<ConnectionRequestResult>> CallAutoConnectAsync(
        OwnerSession sender, CallerKind kind, OdinId recipient)
    {
        var header = new ConnectionRequestHeader
        {
            Recipient = recipient,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>()
        };
        return await CallAutoConnectWithHeaderAsync(sender, kind, header);
    }

    private static async Task<ApiResponse<ConnectionRequestResult>> CallAutoConnectWithHeaderAsync(
        OwnerSession sender, CallerKind kind, ConnectionRequestHeader header)
    {
        if (kind == CallerKind.Owner)
        {
            return await sender.Connections.AutoConnectAsync(header);
        }

        // App caller: register an app on the sender with the Apps permission keys, then call V2
        // AutoConnect under the app's auth scheme.
        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, "auto-connect app drive");
        var app = await AppSession.SetupAsync(sender, drive, DrivePermission.Read, PermissionKeyAllowance.Apps);
        var client = new V2ConnectionRequestsClient(app.Identity, app.Factory);
        return await client.AutoConnectAsync(header);
    }

    private static void AssertOutcome(ConnectionRequestResult result, AutoConnectOutcome expected)
    {
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Outcome, Is.EqualTo(expected), $"detail: {result.Detail}");
    }

    private static async Task AssertBothSidesConnected(OwnerSession sender, OwnerSession recipient)
    {
        var senderIcr = await sender.Connections.GetConnectionInfo(recipient.Identity);
        Assert.That(senderIcr.IsSuccessStatusCode, Is.True);
        Assert.That(senderIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        var recipientIcr = await recipient.Connections.GetConnectionInfo(sender.Identity);
        Assert.That(recipientIcr.IsSuccessStatusCode, Is.True);
        Assert.That(recipientIcr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
    }
}
