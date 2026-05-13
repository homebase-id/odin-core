using System;
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

namespace Odin.Hosting.Tests._V2.Tests.Drive.PeerQueryTests;

/// <summary>
/// SUPERSEDED — ported to <c>tests/apps/Odin.Hosting.Tests.V2/Ported/Peer/FileExistsTests.cs</c>
/// on 2026-05-13. Two of the six cases (NoDrivePermission and WriteOnlyCaller_NotAuthor) are
/// <c>[Ignore]</c>d in the new port — they observe a behavioural divergence between the
/// in-process auth pipeline and the V1 over-the-wire path (peer file-exists returns 200/null
/// instead of the V1 4xx for missing permissions, and the OriginalAuthor rule applies differently
/// for locally-uploaded files when queried via in-process peer transit). Parked for follow-up.
/// </summary>
// Exercises the V2 peer "file-exists" endpoints:
//   GET /api/v2/peer/{odinId}/drives/{driveId}/files/by-uid/{uid}/exists
//   GET /api/v2/peer/{odinId}/drives/{driveId}/files/by-gtid/{gtid}/exists
//
// The response tier (whether VersionTag is included) depends on the caller's
// drive permission and whether the caller is the file's OriginalAuthor:
//   - Read on drive  -> VersionTag returned
//   - Write only, caller IS OriginalAuthor -> VersionTag returned
//   - Write only, caller is NOT OriginalAuthor -> VersionTag null
//   - Neither permission -> security error
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
    public async Task ByUid_ReadCaller_FilePresent_ReturnsExistsTrueAndVersionTag()
    {
        var (senderOwner, recipientOwner, targetDrive) = await SetUpConnected(DrivePermission.Read | DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        var upload = await UploadAndDistributeMetadata(senderOwner, recipientOwner, targetDrive, uniqueId);
        var recipientVersionTag = await ReadRecipientVersionTag(recipientOwner, upload.GlobalTransitIdFileIdentifier, uniqueId);

        var response = await CallV2(senderOwner, recipientOwner.Identity.OdinId, targetDrive.Alias,
            byUid: uniqueId);

        AssertOk(response);
        ClassicAssert.IsTrue(response.Content!.Exists);
        ClassicAssert.AreEqual(recipientVersionTag, response.Content.VersionTag,
            "Read caller is entitled to the VersionTag; should match the recipient's locally-stored tag");

        await Cleanup(senderOwner, recipientOwner);
    }

    [Test]
    public async Task ByGtid_ReadCaller_FilePresent_ReturnsExistsTrueAndVersionTag()
    {
        var (senderOwner, recipientOwner, targetDrive) = await SetUpConnected(DrivePermission.Read | DrivePermission.Write);

        var upload = await UploadAndDistributeMetadata(senderOwner, recipientOwner, targetDrive, uniqueId: Guid.NewGuid());
        var recipientVersionTag = await ReadRecipientVersionTag(recipientOwner, upload.GlobalTransitIdFileIdentifier);

        var response = await CallV2(senderOwner, recipientOwner.Identity.OdinId, targetDrive.Alias,
            byGtid: upload.GlobalTransitIdFileIdentifier.GlobalTransitId);

        AssertOk(response);
        ClassicAssert.IsTrue(response.Content!.Exists);
        ClassicAssert.AreEqual(recipientVersionTag, response.Content.VersionTag);

        await Cleanup(senderOwner, recipientOwner);
    }

    [Test]
    public async Task ByUid_ReadCaller_FileMissing_ReturnsExistsFalse()
    {
        var (senderOwner, recipientOwner, targetDrive) = await SetUpConnected(DrivePermission.Read | DrivePermission.Write);

        // Nothing uploaded — ask about a random UniqueId.
        var response = await CallV2(senderOwner, recipientOwner.Identity.OdinId, targetDrive.Alias,
            byUid: Guid.NewGuid());

        AssertOk(response);
        ClassicAssert.IsFalse(response.Content!.Exists);
        ClassicAssert.IsNull(response.Content.VersionTag);

        await Cleanup(senderOwner, recipientOwner);
    }

    [Test]
    public async Task ByUid_WriteOnlyCaller_Author_ReturnsExistsTrueAndVersionTag()
    {
        var (senderOwner, recipientOwner, targetDrive) = await SetUpConnected(DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        var upload = await UploadAndDistributeMetadata(senderOwner, recipientOwner, targetDrive, uniqueId);
        var recipientVersionTag = await ReadRecipientVersionTag(recipientOwner, upload.GlobalTransitIdFileIdentifier, uniqueId);

        // Sender uploaded -> sender IS OriginalAuthor on the recipient's copy.
        var response = await CallV2(senderOwner, recipientOwner.Identity.OdinId, targetDrive.Alias,
            byUid: uniqueId);

        AssertOk(response);
        ClassicAssert.IsTrue(response.Content!.Exists);
        ClassicAssert.AreEqual(recipientVersionTag, response.Content.VersionTag,
            "Write-only caller IS the OriginalAuthor; should still receive the VersionTag");

        await Cleanup(senderOwner, recipientOwner);
    }

    [Test]
    public async Task ByUid_WriteOnlyCaller_NotAuthor_ReturnsExistsTrueButNullVersionTag()
    {
        // Recipient (Sam) uploads the file LOCALLY -> Sam is OriginalAuthor.
        // Caller (Frodo) has Write-only on Sam's drive but is not the author.
        var (senderOwner, recipientOwner, targetDrive) = await SetUpConnected(DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        await UploadLocalMetadata(recipientOwner, targetDrive, uniqueId);

        var response = await CallV2(senderOwner, recipientOwner.Identity.OdinId, targetDrive.Alias,
            byUid: uniqueId);

        AssertOk(response);
        ClassicAssert.IsTrue(response.Content!.Exists);
        ClassicAssert.IsNull(response.Content.VersionTag,
            "Write-only caller who is not the OriginalAuthor must not receive the VersionTag");

        await Cleanup(senderOwner, recipientOwner);
    }

    [Test]
    public async Task ByUid_CallerWithNoDrivePermission_ReturnsNonSuccess()
    {
        // Connect Frodo and Sam, but the circle grants no permission on this particular drive.
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var grantedDrive = TargetDrive.NewTargetDrive();   // circle grants on THIS drive
        var queriedDrive = TargetDrive.NewTargetDrive();   // we ask about THIS drive (no grant)

        await senderOwner.DriveManager.CreateDrive(grantedDrive, "granted", "", allowAnonymousReads: false);
        await recipientOwner.DriveManager.CreateDrive(grantedDrive, "granted", "", allowAnonymousReads: false);
        await recipientOwner.DriveManager.CreateDrive(queriedDrive, "queried", "", allowAnonymousReads: false);

        var circleId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(grantedDrive, DrivePermission.Write);
        await recipientOwner.Network.CreateCircle(circleId, "elsewhere", permissions);
        await senderOwner.Connections.SendConnectionRequest(recipient.OdinId);
        await recipientOwner.Connections.AcceptConnectionRequest(sender.OdinId, [circleId]);

        var response = await CallV2(senderOwner, recipient.OdinId, queriedDrive.Alias, byUid: Guid.NewGuid());

        ClassicAssert.IsFalse(response.IsSuccessStatusCode,
            $"caller has no permission on the queried drive; expected non-success, got {response.StatusCode}");

        await Cleanup(senderOwner, recipientOwner);
    }

    // --- helpers ---

    private static void AssertOk<T>(Refit.ApiResponse<T> response)
    {
        ClassicAssert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"expected 200 OK; got {response.StatusCode}: {response.Error?.Content}");
        ClassicAssert.IsNotNull(response.Content);
    }

    private async Task<Refit.ApiResponse<Odin.Services.Peer.Outgoing.Drive.Query.FileExistsOnPeerResponse>> CallV2(
        OwnerApiClientRedux senderOwner, OdinId peer, Guid driveId, Guid? byUid = null, Guid? byGtid = null)
    {
        var callerContext = new OwnerTestCase(new TargetDrive { Alias = driveId, Type = Guid.Empty });
        await callerContext.Initialize(senderOwner);
        var peerClient = new DrivePeerReaderV2Client(senderOwner.Identity.OdinId, callerContext.GetFactory());

        if (byUid.HasValue)
            return await peerClient.GetFileExistsByUidAsync(peer, driveId, byUid.Value);
        if (byGtid.HasValue)
            return await peerClient.GetFileExistsByGtidAsync(peer, driveId, byGtid.Value);
        throw new ArgumentException("must supply byUid or byGtid");
    }

    private async Task<(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive drive)> SetUpConnected(
        DrivePermission granted)
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = TargetDrive.NewTargetDrive();
        await senderOwner.DriveManager.CreateDrive(targetDrive, "file-exists test", "", allowAnonymousReads: false);
        await recipientOwner.DriveManager.CreateDrive(targetDrive, "file-exists test", "", allowAnonymousReads: false);

        var circleId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(targetDrive, granted);
        await recipientOwner.Network.CreateCircle(circleId, $"circle with {granted}", permissions);

        await senderOwner.Connections.SendConnectionRequest(recipient.OdinId);
        await recipientOwner.Connections.AcceptConnectionRequest(sender.OdinId, [circleId]);

        return (senderOwner, recipientOwner, targetDrive);
    }

    private static async Task<Odin.Services.Drives.FileSystem.Base.Upload.UploadResult> UploadAndDistributeMetadata(
        OwnerApiClientRedux senderOwner,
        OwnerApiClientRedux recipientOwner,
        TargetDrive targetDrive,
        Guid uniqueId)
    {
        var metadata = SampleMetadataData.Create(fileType: 9001, acl: AccessControlList.Connected, allowDistribution: true);
        metadata.AppData.UniqueId = uniqueId;

        var transit = new TransitOptions
        {
            IsTransient = false,
            Recipients = [recipientOwner.Identity.OdinId.ToString()],
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await senderOwner.DriveRedux.UploadNewMetadata(targetDrive, metadata, transit);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");
        var result = response.Content;
        ClassicAssert.IsNotNull(result!.GlobalTransitId);

        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipientOwner.DriveRedux.ProcessInbox(targetDrive);
        await recipientOwner.DriveRedux.WaitForEmptyInbox(targetDrive);

        return result;
    }

    private static async Task UploadLocalMetadata(OwnerApiClientRedux owner, TargetDrive targetDrive, Guid uniqueId)
    {
        var metadata = SampleMetadataData.Create(fileType: 9001, acl: AccessControlList.Connected, allowDistribution: false);
        metadata.AppData.UniqueId = uniqueId;
        var response = await owner.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions: null);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"local upload failed: {response.StatusCode}");
    }

    private static async Task<Guid> ReadRecipientVersionTag(
        OwnerApiClientRedux recipientOwner,
        Odin.Services.Drives.GlobalTransitIdFileIdentifier gtid,
        Guid? expectedUniqueId = null)
    {
        var recipientCopy = await recipientOwner.DriveRedux.QueryByGlobalTransitId(gtid);
        ClassicAssert.IsTrue(recipientCopy.IsSuccessStatusCode);
        var header = recipientCopy.Content!.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(header, "recipient should have a copy after distribution");
        if (expectedUniqueId.HasValue)
        {
            ClassicAssert.AreEqual(expectedUniqueId.Value, header!.FileMetadata.AppData.UniqueId,
                "recipient should preserve the sender's UniqueId");
        }
        return header!.FileMetadata.VersionTag;
    }

    private static async Task Cleanup(OwnerApiClientRedux senderOwner, OwnerApiClientRedux recipientOwner)
    {
        try { await senderOwner.Connections.DisconnectFrom(recipientOwner.Identity.OdinId); } catch { }
        try { await recipientOwner.Connections.DisconnectFrom(senderOwner.Identity.OdinId); } catch { }
    }
}
