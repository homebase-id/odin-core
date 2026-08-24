using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// Cat 2 - the recut 777 tier.  A `connected` ACL used to admit every connection, reviewed or not;
/// these pin that it now admits only the reviewed, without closing the perimeter on anyone.
/// </summary>
public class ReviewedTierTests
{
    private WebScaffold _scaffold;

    private readonly TargetDrive _drive = TargetDrive.NewTargetDrive();
    // A distinct file type per test: the fixture shares one drive, so files would otherwise accumulate
    // across tests and the counts would drift.
    private const int AclFileType = 8811;
    private const int RestoreFileType = 8812;
    private const int RevokeFileType = 8813;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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

    [Test]
    public async Task AReviewedConnectionCanReadAConnectedAclFileAndAnUnreviewedOneCannot()
    {
        // The DB path, not just the evaluator: QueryBatch filters on
        // requiredSecurityGroup BETWEEN 0 AND callerLevel before any ACL check runs.
        var (frodo, sam) = await Connect();
        await frodo.DriveManager.CreateDrive(_drive, "reviewed tier", "", allowAnonymousReads: true);
        await UploadReviewedOnlyFile(frodo, AclFileType);

        var reviewed = await sam.PeerQuery.GetBatch(Query(AclFileType));
        ClassicAssert.IsTrue(reviewed.IsSuccessStatusCode);
        ClassicAssert.AreEqual(1, reviewed.Content.SearchResults.Count(),
            "a reviewed connection should see a file ACL'd to the reviewed tier");

        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode, $"un-review failed: {unreview.Error?.Content}");

        var unreviewed = await sam.PeerQuery.GetBatch(Query(AclFileType));
        ClassicAssert.IsTrue(unreviewed.IsSuccessStatusCode, "the query itself still succeeds");
        ClassicAssert.IsFalse(unreviewed.Content.SearchResults.Any(),
            "an unreviewed connection must not see it -- this is the tier doing what it always claimed");

        await Cleanup();
    }

    [Test]
    public async Task ReviewingRestoresAccessImmediately()
    {
        // Peer contexts are cached for an hour. A promotion that only takes effect when the cache
        // expires is a promotion the owner cannot observe.
        var (frodo, sam) = await Connect();
        await frodo.DriveManager.CreateDrive(_drive, "reviewed tier", "", allowAnonymousReads: true);
        await UploadReviewedOnlyFile(frodo, RestoreFileType);

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var denied = await sam.PeerQuery.GetBatch(Query(RestoreFileType));
        ClassicAssert.IsFalse(denied.Content.SearchResults.Any(), "precondition: access is gone");

        var review = await frodo.Network.ReviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode);

        var restored = await sam.PeerQuery.GetBatch(Query(RestoreFileType));
        ClassicAssert.AreEqual(1, restored.Content.SearchResults.Count(),
            "the review must take effect now, not when the peer context cache expires");

        await Cleanup();
    }

    [Test]
    public async Task UnReviewingRemovesAccessImmediately()
    {
        // The direction where being slow is a security problem.
        var (frodo, sam) = await Connect();
        await frodo.DriveManager.CreateDrive(_drive, "reviewed tier", "", allowAnonymousReads: true);
        await UploadReviewedOnlyFile(frodo, RevokeFileType);

        var granted = await sam.PeerQuery.GetBatch(Query(RevokeFileType));
        ClassicAssert.AreEqual(1, granted.Content.SearchResults.Count(), "precondition: access exists");

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var revoked = await sam.PeerQuery.GetBatch(Query(RevokeFileType));
        ClassicAssert.IsFalse(revoked.Content.SearchResults.Any(),
            "the demotion must take effect now, not up to an hour later");

        await Cleanup();
    }

    [Test]
    public async Task TheTierAssignedOverTransitFollowsTheReviewStamp()
    {
        // Straight at the assignment: Sam asks Frodo what security level Frodo's server gives him.
        var (frodo, sam) = await Connect();

        var stamped = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(stamped.Content.ReviewedAt,
            "precondition: an owner-driven request leaves the requester's side reviewed too");

        var asReviewed = await sam.PeerQuery.GetRemoteDotYouContext(
            new TransitGetSecurityContextRequest { OdinId = frodo.OdinId });

        ClassicAssert.IsTrue(asReviewed.IsSuccessStatusCode);
        ClassicAssert.AreEqual(SecurityGroupType.Reviewed, asReviewed.Content.Caller.SecurityLevel,
            "a reviewed connection should rank at the reviewed tier");

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var asUnreviewed = await sam.PeerQuery.GetRemoteDotYouContext(
            new TransitGetSecurityContextRequest { OdinId = frodo.OdinId });

        ClassicAssert.IsTrue(asUnreviewed.IsSuccessStatusCode,
            "an unreviewed connection is still connected -- the call itself must succeed");
        ClassicAssert.AreEqual(SecurityGroupType.Authenticated, asUnreviewed.Content.Caller.SecurityLevel,
            "an unreviewed connection reads nothing beyond any logged-in identity");

        await Cleanup();
    }

    [Test]
    public async Task AnUnreviewedConnectionCanStillSendAFileOverPeer()
    {
        // The 3 a.m. case, and the reason Caller.IsConnected had to stop deriving from SecurityLevel.
        // PeerIncomingDriveUploadController gates the write on IsConnected; an unreviewed connection
        // ranks Authenticated for reads but is still connected at the wire, and must still be able to
        // deposit. If those two questions ever collapse back into one field, this fails.
        var (frodo, sam) = await Connect();

        var unreview = await sam.Network.UnreviewConnection(frodo.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode, $"un-review failed: {unreview.Error?.Content}");

        var targetDrive = SystemDriveConstants.ChatDrive;
        var fileMetadata = SampleMetadataData.CreateWithContent(9001, "hello at 3am", AccessControlList.Reviewed);
        fileMetadata.AllowDistribution = true;

        var (sendResponse, _) = await frodo.DriveRedux.UploadNewEncryptedMetadata(
            fileMetadata,
            new StorageOptions { Drive = targetDrive },
            new TransitOptions { Recipients = [sam.OdinId], Priority = OutboxPriority.High });

        ClassicAssert.IsTrue(sendResponse.IsSuccessStatusCode);
        ClassicAssert.AreEqual(TransferStatus.Enqueued, sendResponse.Content.RecipientStatus[sam.OdinId]);

        await frodo.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await sam.DriveRedux.ProcessInbox(targetDrive);

        var landed = await sam.DriveRedux.QueryByGlobalTransitId(
            sendResponse.Content.GlobalTransitIdFileIdentifier);

        ClassicAssert.IsTrue(landed.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(landed.Content.SearchResults.SingleOrDefault(),
            "the perimeter closed on a sender the recipient has not reviewed");

        await Cleanup();
    }

    [Test]
    public async Task GrantingACircleToAnUnreviewedContactPromotesThemImmediately()
    {
        // Putting someone in a circle stamps the review as a side effect -- membership implies review.
        // The promotion has to reach the peer's cached transit context, or the owner performs a
        // deliberate act and the contact keeps the old level for up to an hour.
        var (frodo, sam) = await Connect();

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var before = await sam.PeerQuery.GetRemoteDotYouContext(
            new TransitGetSecurityContextRequest { OdinId = frodo.OdinId });
        ClassicAssert.AreEqual(SecurityGroupType.Authenticated, before.Content.Caller.SecurityLevel,
            "precondition: unreviewed");

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var granted = await frodo.Network.GrantCircle(circleId, sam.OdinId);
        ClassicAssert.IsTrue(granted.IsSuccessStatusCode, $"grant failed: {granted.Error?.Content}");

        var stamped = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(stamped.Content.ReviewedAt, "the grant should have stamped the review");

        var after = await sam.PeerQuery.GetRemoteDotYouContext(
            new TransitGetSecurityContextRequest { OdinId = frodo.OdinId });
        ClassicAssert.AreEqual(SecurityGroupType.Reviewed, after.Content.Caller.SecurityLevel,
            "the promotion must reach the peer's cached context, not wait for it to expire");

        await Cleanup();
    }

    private PeerQueryBatchRequest Query(int fileType)
    {
        return new PeerQueryBatchRequest
        {
            OdinId = TestIdentities.Frodo.OdinId,
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = _drive,
                FileType = [fileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };
    }

    private async Task UploadReviewedOnlyFile(OwnerApiClientRedux frodo, int fileType)
    {
        var response = await frodo.DriveRedux.UploadNewMetadata(_drive, new UploadFileMetadata
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { FileType = fileType, Content = "reviewed eyes only" },

            // The bare `connected` ACL -- serialized as "connected", now meaning the reviewed tier.
            AccessControlList = AccessControlList.Reviewed
        });

        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.Error?.Content}");
    }

    private async Task<(OwnerApiClientRedux frodo, OwnerApiClientRedux sam)> Connect()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        return (frodo, sam);
    }

    private async Task Cleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
    }
}
