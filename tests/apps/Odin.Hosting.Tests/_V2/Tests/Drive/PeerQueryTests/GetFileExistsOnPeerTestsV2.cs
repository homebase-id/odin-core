using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Query;

namespace Odin.Hosting.Tests._V2.Tests.Drive.PeerQueryTests;

// Exercises V2DrivePeerQueryController.GetFileExists:
//   POST /api/v2/peer/drives/{driveId:guid}/files/file-exists
// The sender uploads a metadata-only file with a UniqueId, fans it out to a
// connected recipient, then asks the V2 endpoint whether the recipient has it.
public class GetFileExistsOnPeerTestsV2
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
            [TestIdentities.Frodo, TestIdentities.Samwise]);
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

    [Test]
    public async Task GetFileExists_WhenRecipientHasMatchingUidAndVersionTag_ReturnsTrue()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var callerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = callerContext.TargetDrive;
        await PrepareScenario(senderOwner, recipientOwner, targetDrive);

        var uniqueId = Guid.NewGuid();
        var upload = await UploadAndDistributeMetadata(senderOwner, recipientOwner, targetDrive,
            fileType: 9001, uniqueId: uniqueId, recipient.OdinId);

        // Look up the recipient's copy. UniqueId is preserved across the transfer, but
        // the recipient assigns its own VersionTag on receipt — that locally-stored
        // VersionTag is what the receiving handler compares against, so it's what the
        // caller must supply.
        var recipientCopy = await recipientOwner.DriveRedux.QueryByGlobalTransitId(upload.GlobalTransitIdFileIdentifier);
        ClassicAssert.IsTrue(recipientCopy.IsSuccessStatusCode);
        var recipientHeader = recipientCopy.Content!.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(recipientHeader, "recipient should have a copy after distribution");
        ClassicAssert.AreEqual(uniqueId, recipientHeader!.FileMetadata.AppData.UniqueId,
            "recipient's copy should preserve the sender's UniqueId");
        var recipientVersionTag = recipientHeader.FileMetadata.VersionTag;

        await callerContext.Initialize(senderOwner);
        var peerClient = new DrivePeerReaderV2Client(sender.OdinId, callerContext.GetFactory());

        var response = await peerClient.GetFileExistsAsync(targetDrive.Alias,
            new PeerFileExistsByUidAndVersionTagRequest
            {
                OdinId = recipient.OdinId,
                UniqueId = uniqueId,
                VersionTag = recipientVersionTag
            });

        ClassicAssert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"V2 peer file-exists should return 200 OK; got {response.StatusCode}");
        ClassicAssert.IsTrue(response.Content,
            "V2 peer file-exists should report true when the recipient has the file at the same VersionTag");

        await Cleanup(senderOwner, recipientOwner);
    }

    private async Task<Odin.Services.Drives.FileSystem.Base.Upload.UploadResult> UploadAndDistributeMetadata(
        OwnerApiClientRedux senderOwner,
        OwnerApiClientRedux recipientOwner,
        TargetDrive targetDrive,
        int fileType,
        Guid uniqueId,
        OdinId recipient)
    {
        var metadata = SampleMetadataData.Create(
            fileType: fileType,
            acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.UniqueId = uniqueId;

        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = [recipient.ToString()],
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await senderOwner.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);
        ClassicAssert.IsNotNull(uploadResult!.GlobalTransitId, "uploaded file is missing a GlobalTransitId");

        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipientOwner.DriveRedux.ProcessInbox(targetDrive);
        await recipientOwner.DriveRedux.WaitForEmptyInbox(targetDrive);

        return uploadResult;
    }

    private static async Task PrepareScenario(
        OwnerApiClientRedux senderOwner,
        OwnerApiClientRedux recipientOwner,
        TargetDrive targetDrive)
    {
        await senderOwner.DriveManager.CreateDrive(targetDrive, "Peer file-exists test drive", "",
            allowAnonymousReads: false);
        await recipientOwner.DriveManager.CreateDrive(targetDrive, "Peer file-exists test drive", "",
            allowAnonymousReads: false);

        // Grant the sender Write on the recipient's drive so the peer transfer can land.
        // The receiving file-exists handler accepts either Read or Write on the drive.
        var circleId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(targetDrive, DrivePermission.Write);
        await recipientOwner.Network.CreateCircle(circleId, "circle with write access", permissions);

        await senderOwner.Connections.SendConnectionRequest(recipientOwner.Identity.OdinId);
        await recipientOwner.Connections.AcceptConnectionRequest(senderOwner.Identity.OdinId, [circleId]);
    }

    private static async Task Cleanup(OwnerApiClientRedux senderOwner, OwnerApiClientRedux recipientOwner)
    {
        try { await senderOwner.Connections.DisconnectFrom(recipientOwner.Identity.OdinId); } catch { }
        try { await recipientOwner.Connections.DisconnectFrom(senderOwner.Identity.OdinId); } catch { }
    }
}
