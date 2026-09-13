using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

/// <summary>
/// Ttl must cross peer. This is the entire reason it lives on FileMetadata rather than ServerMetadata:
/// PeerFileWriter deserializes FileMetadata whole from the transfer, while it builds a fresh
/// ServerMetadata on receipt. Without this, a group could not expire its members' copies of a message.
/// </summary>
public class DirectDriveFileTtlPeerTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Samwise });
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
    public async Task AnAbsoluteTtlSurvivesTheHopToARecipient()
    {
        // The chat-retention shape: resolved to an absolute time at send, so every member's copy dies
        // at the same moment without any retention message being exchanged.
        var ttl = FileTtl.After(TimeSpan.FromDays(90));

        var recipientTtl = await SendWithTtlAndReadBack(ttl, fileType: 7001);

        ClassicAssert.AreEqual(ttl, recipientTtl, "the recipient's copy must carry the sender's Ttl");
    }

    [Test]
    public async Task APendingTtlCrossesPeerStillPendingSoEachCopyRunsItsOwnClock()
    {
        // The Snapchat shape. It must arrive still negative: if it had been resolved on the sender it
        // would die by the sender's reading habits rather than the recipient's.
        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var recipientTtl = await SendWithTtlAndReadBack(ttl, fileType: 7002);

        ClassicAssert.AreEqual(ttl, recipientTtl, "a pending Ttl must arrive unresolved");
        ClassicAssert.IsTrue(FileTtl.IsPendingFirstRead(recipientTtl));
    }

    /// <summary>
    /// The Snapchat requirement proper: each copy's clock starts on its own reader's first view. The
    /// recipient reading their payload must resolve *their* copy and leave the sender's alone.
    /// </summary>
    [Test]
    public async Task EachCopyResolvesItsPendingTtlIndependentlyOnItsOwnFirstRead()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = "burn", FileType = 7003 },
            AccessControlList = AccessControlList.Connected,
            Ttl = ttl
        };

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var uploadResponse = await sender.DriveRedux.UploadNewFile(targetDrive, fileMetadata, manifest, payloads,
            new TransitOptions { Recipients = [recipient.Identity.OdinId] });
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipient.DriveRedux.ProcessInbox(targetDrive);

        var recipientCopy = (await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult!.GlobalTransitIdFileIdentifier))
            .Content!.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(recipientCopy);
        ClassicAssert.IsTrue(FileTtl.IsPendingFirstRead(recipientCopy!.FileMetadata.Ttl), "arrives unresolved");

        // the recipient opens it
        var recipientFile = new ExternalFileIdentifier
        {
            FileId = recipientCopy.FileId,
            TargetDrive = targetDrive
        };
        ClassicAssert.IsTrue((await recipient.DriveRedux.GetPayload(recipientFile, payload.Key)).IsSuccessStatusCode);

        var recipientAfter = (await recipient.DriveRedux.GetFileHeader(recipientFile)).Content;
        ClassicAssert.IsTrue(FileTtl.IsAbsolute(recipientAfter!.FileMetadata.Ttl),
            "the recipient's own read must start the recipient's clock");

        // ...and the sender, who has not opened theirs, is untouched
        var senderAfter = (await sender.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        ClassicAssert.AreEqual(ttl, senderAfter!.FileMetadata.Ttl,
            "the sender's copy must still be pending - one reader must not burn another's copy");

        await _scaffold.OldOwnerApi.DisconnectIdentities(sender.Identity.OdinId, recipient.Identity.OdinId);
    }

    private async Task<long> SendWithTtlAndReadBack(long ttl, int fileType)
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new UploadAppFileMetaData { Content = "expiring", FileType = fileType },
            AccessControlList = AccessControlList.Connected,
            Ttl = ttl
        };

        var (uploadResponse, _) = await sender.DriveRedux.UploadNewEncryptedMetadata(
            fileMetadata,
            new StorageOptions { Drive = targetDrive },
            new TransitOptions { Recipients = [recipient.Identity.OdinId] });

        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipient.DriveRedux.ProcessInbox(targetDrive);

        // The recipient holds a different FileId, so the copy has to be found by global transit id
        var queryResponse = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult!.GlobalTransitIdFileIdentifier);
        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode);

        var recipientCopy = queryResponse.Content!.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(recipientCopy, "the recipient should have received the file");

        // the sender's own copy is untouched by the hop
        var senderHeader = (await sender.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        ClassicAssert.AreEqual(ttl, senderHeader!.FileMetadata.Ttl, "the sender's copy must keep its Ttl");

        await _scaffold.OldOwnerApi.DisconnectIdentities(sender.Identity.OdinId, recipient.Identity.OdinId);

        return recipientCopy!.FileMetadata.Ttl;
    }

    private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive, DrivePermission drivePermissions)
    {
        await recipientOwnerClient.DriveManager.CreateDrive(targetDrive, "Target drive on recipient", "", false, false, false);
        await senderOwnerClient.DriveManager.CreateDrive(targetDrive, "Target drive on sender", "", false, false, false);

        var expectedPermissionedDrive = new PermissionedDrive { Drive = targetDrive, Permission = drivePermissions };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest> { new() { PermissionedDrive = expectedPermissionedDrive } }
            });

        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>());
        await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });
    }
}
