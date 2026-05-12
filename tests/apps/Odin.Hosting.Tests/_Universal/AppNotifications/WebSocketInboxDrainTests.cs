using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Verifies the server-side inbox drain triggered from the WebSocket
// InboxItemReceivedNotification path. The recipient deliberately does NOT call
// ProcessInbox — the only way FileAdded can show up over the WS is the new
// drain in AppNotificationHandler.WsPublishAsync(InboxItemReceivedNotificationMessage).
public class WebSocketInboxDrainTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
            { TestIdentities.Frodo, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    [Test]
    public async Task PeerFileArrival_TriggersServerSideDrain_FileAddedDeliveredOverWebSocket()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var handler = new WebSocketDrainTestSocketHandler();
        await handler.ConnectAsync(recipient, targetDrive);

        try
        {
            var uploadResult = await SendFileFromSenderToRecipient(sender, recipient, targetDrive, fileType: 8801);

            // Sender's outbox is now empty — the file is on the recipient host's inbox.
            // We deliberately do NOT call recipient.DriveRedux.ProcessInbox. The server's
            // WS notify path should drain on its own and emit FileAdded.
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            var arrived = await handler.WaitForFileAdded(
                uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId,
                TimeSpan.FromSeconds(15));

            ClassicAssert.IsTrue(arrived,
                "Server-side WS drain did not run. The recipient WS should observe FileAdded " +
                "without ProcessInbox being called. If this fails, " +
                "AppNotificationHandler.TryDrainInboxForDriveAsync is not draining the inbox " +
                "before fanning out the InboxItemReceived notification.");

            // Drain not just notified but actually processed: file should be queryable.
            var query = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            ClassicAssert.IsTrue(query.IsSuccessStatusCode, $"QueryByGlobalTransitId failed: {query.StatusCode}");
            var hit = query.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(hit, "File was advertised via FileAdded but is not queryable on recipient.");
        }
        finally
        {
            await handler.DisconnectAsync();
            await DeleteScenario(sender, recipient);
        }
    }

    [Test]
    public async Task MultiplePeerFileArrivalsInBurst_AllDeliveredOverWebSocket()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var handler = new WebSocketDrainTestSocketHandler();
        await handler.ConnectAsync(recipient, targetDrive);

        try
        {
            // Fire three uploads back-to-back. Each produces an InboxItemReceived on the
            // recipient; the in-flight coalescer must not lose any of the resulting drains.
            var uploads = await Task.WhenAll(
                SendFileFromSenderToRecipient(sender, recipient, targetDrive, fileType: 8810),
                SendFileFromSenderToRecipient(sender, recipient, targetDrive, fileType: 8811),
                SendFileFromSenderToRecipient(sender, recipient, targetDrive, fileType: 8812));

            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive, TimeSpan.FromMinutes(1));

            var expected = uploads.Select(u => u.GlobalTransitIdFileIdentifier.GlobalTransitId).ToList();
            var allArrived = await handler.WaitForAllFilesAdded(expected, TimeSpan.FromSeconds(30));

            var observed = handler.FileAddedEvents
                .Select(e => e.Header.FileMetadata?.GlobalTransitId)
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .ToList();

            ClassicAssert.IsTrue(allArrived,
                $"Expected FileAdded for all {expected.Count} uploaded files within timeout. " +
                $"Got {observed.Count}: [{string.Join(", ", observed)}]. Coalescer may have dropped work.");

            CollectionAssert.IsSubsetOf(expected, observed,
                "Each uploaded GlobalTransitId must appear in the observed FileAdded events.");
        }
        finally
        {
            await handler.DisconnectAsync();
            await DeleteScenario(sender, recipient);
        }
    }

    //
    // Note: a planned back-compat test that pinned ClientNotificationType.InboxItemReceived
    // delivery to owner WS was dropped after verifying empirically (with the drain call
    // temporarily disabled) that InboxItemReceived does NOT reach the owner-WS client in
    // this scenario — pre-existing behavior, independent of the drain. Only FileAdded
    // events arrive over the owner WS. This reinforces the redundancy TODO on
    // AppNotificationHandler.Handle(InboxItemReceivedNotification): there is effectively
    // no owner-WS back-compat surface to preserve for this notification.
    //

    //
    // Helpers — borrowed from InboxDrainOnQueryTests
    //

    private async Task<UploadResult> SendFileFromSenderToRecipient(
        OwnerApiClientRedux sender,
        OwnerApiClientRedux recipient,
        TargetDrive targetDrive,
        int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AllowDistribution = true;

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity.OdinId]
        };

        var response = await sender.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");

        var result = response.Content;
        ClassicAssert.IsTrue(result!.RecipientStatus.Count == 1);
        ClassicAssert.IsTrue(result.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued);

        return result;
    }

    private async Task PrepareScenario(
        OwnerApiClientRedux senderOwnerClient,
        OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive,
        DrivePermission drivePermissions)
    {
        var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on sender",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

        var permissionedDrive = new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = drivePermissions
        };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId,
            "Circle with drive access",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new() { PermissionedDrive = permissionedDrive }
                }
            });
        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        await senderOwnerClient.Connections.SendConnectionRequest(
            recipientOwnerClient.Identity.OdinId, new List<GuidId>());

        await recipientOwnerClient.Connections.AcceptConnectionRequest(
            senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });
    }

    private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(
            senderOwnerClient.Identity.OdinId,
            recipientOwnerClient.Identity.OdinId);
    }
}
