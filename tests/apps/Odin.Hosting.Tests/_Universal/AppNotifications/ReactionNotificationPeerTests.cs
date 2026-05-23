using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Verifies the peer-incoming reaction path: when a connected identity distributes a reaction to us,
// our owner WebSocket receives a single fileModified (the reaction summary update, formerly
// statisticsChanged) and no second notification (peer reactions never write localReactions).
public class ReactionNotificationPeerTests
{
    private WebScaffold _scaffold;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
    private const string Reaction1 = ":thumbsup:";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Samwise });
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
    public async Task PeerIncomingReaction_RecipientReceivesExactlyOneFileModified_AndNoStatisticsChanged()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write | DrivePermission.React);

        // Distribute a file to the recipient so the reaction has a local target there.
        var upload = await SendFileToRecipient(sender, recipient, targetDrive, fileType: 7001);
        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipient.DriveRedux.ProcessInbox(targetDrive);

        var gtid = upload.GlobalTransitIdFileIdentifier.GlobalTransitId;

        var handler = new ReactionNotificationSocketHandler();
        await handler.ConnectAsync(recipient, targetDrive);

        try
        {
            // Sender reacts and distributes the reaction to the recipient (peer-incoming on recipient).
            var addReaction = await sender.Reactions.AddReaction(new AddReactionRequestRedux
            {
                File = upload.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
                Reaction = Reaction1,
                TransitOptions = new ReactionTransitOptions { Recipients = new List<OdinId> { recipient.Identity.OdinId } }
            });
            ClassicAssert.IsTrue(addReaction.IsSuccessStatusCode, $"AddReaction failed: {addReaction.StatusCode}");

            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
            await recipient.DriveRedux.ProcessInbox(targetDrive);

            var fileModified = await handler.WaitForNotification(ClientNotificationType.FileModified, gtid, WaitTimeout);
            ClassicAssert.IsNotNull(fileModified, "Recipient did not receive fileModified for the peer reaction.");
            await Task.Delay(SettleDelay);

            ClassicAssert.AreEqual(1, handler.CountByType(ClientNotificationType.FileModified, gtid),
                "A peer-incoming reaction should produce exactly one fileModified on the recipient.");
            ClassicAssert.AreEqual(0, handler.CountByType(ClientNotificationType.StatisticsChanged, gtid),
                "statisticsChanged is retired; a peer-incoming reaction must not send it.");
        }
        finally
        {
            await handler.DisconnectAsync();
            await DeleteScenario(sender, recipient);
        }
    }

    //
    // Helpers (adapted from WebSocketInboxDrainTests)
    //

    private async Task<UploadResult> SendFileToRecipient(
        OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive targetDrive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AllowDistribution = true;

        var transitOptions = new TransitOptions { Recipients = [recipient.Identity.OdinId] };
        var response = await sender.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");
        return response.Content;
    }

    private async Task PrepareScenario(
        OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive, DrivePermission drivePermissions)
    {
        await recipientOwnerClient.DriveManager.CreateDrive(targetDrive, "Target drive on recipient", "",
            allowAnonymousReads: false, allowSubscriptions: false, ownerOnly: false);
        await senderOwnerClient.DriveManager.CreateDrive(targetDrive, "Target drive on sender", "",
            allowAnonymousReads: false, allowSubscriptions: false, ownerOnly: false);

        var permissionedDrive = new PermissionedDrive { Drive = targetDrive, Permission = drivePermissions };
        var circleId = Guid.NewGuid();
        await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest> { new() { PermissionedDrive = permissionedDrive } }
            });

        await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>());
        await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });
    }

    private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
    }
}
