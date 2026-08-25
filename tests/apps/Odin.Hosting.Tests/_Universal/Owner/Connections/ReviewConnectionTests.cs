using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Authorization.Permissions;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// Cat 1 of docs/connection-defaults-checklist.md -- the review stamp.
/// </summary>
public class ReviewConnectionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise,
            TestIdentities.Merry
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

    [Test]
    public async Task OwnerAcceptedConnectionIsStampedReviewed()
    {
        var (frodo, sam) = await Connect();

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(info.IsSuccessStatusCode);

        // An owner-driven request is the review happening at accept time.
        ClassicAssert.IsNotNull(info.Content.ReviewedAt, "owner-accepted connection should carry a review stamp");
        ClassicAssert.IsTrue(info.Content.Vetted, "legacy Vetted must stay true for an owner-accepted connection");

        await Cleanup();
    }

    [Test]
    public async Task ChatOnlyReviewStampsWithoutGrantingCircles()
    {
        var (frodo, sam) = await Connect();

        // Un-review first so we start from New.
        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode, $"un-review failed: {unreview.Error?.Content}");

        var asNew = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNull(asNew.Content.ReviewedAt);
        ClassicAssert.IsFalse(asNew.Content.Vetted);

        var grantCountBefore = asNew.Content.AccessGrant.CircleGrants.Count;

        // No circles == the "chat only" outcome: it records the decision and grants nothing the owner
        // did not choose. Membership of the Reviewed Connections circle is not a choice -- it is what
        // reviewing means -- so the contact joins that one, and only that one.
        var review = await frodo.Network.ReviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var reviewed = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(reviewed.Content.ReviewedAt);
        ClassicAssert.IsTrue(reviewed.Content.Vetted);
        ClassicAssert.AreEqual(grantCountBefore + 1, reviewed.Content.AccessGrant.CircleGrants.Count,
            "a chat-only review must add them to the Reviewed circle and nothing else");
        ClassicAssert.IsTrue(reviewed.Content.AccessGrant.CircleGrants.Exists(
            g => g.CircleId == SystemCircleConstants.ReviewedConnectionsCircleId));

        await Cleanup();
    }

    [Test]
    public async Task ReviewEnrollsTheReviewedCircle_WithTheShardDriveGrant()
    {
        var (frodo, sam) = await Connect();

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var review = await frodo.Network.ReviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        var grant = info.Content.AccessGrant.CircleGrants.Find(
            g => g.CircleId == SystemCircleConstants.ReviewedConnectionsCircleId);

        ClassicAssert.IsNotNull(grant, "reviewing must add them to the Reviewed circle");

        // The point of the circle: a reviewed contact may write their recovery shards to us.
        var shardGrant = grant.DriveGrants.Find(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.ShardRecoveryDrive &&
            dg.PermissionedDrive.Permission == DrivePermission.Write);
        ClassicAssert.IsNotNull(shardGrant, "the Reviewed circle must carry write on the shard drive");

        await Cleanup();
    }

    [Test]
    public async Task UnreviewRevokesTheReviewedCircle()
    {
        var (frodo, sam) = await Connect();

        var review = await frodo.Network.ReviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode);

        var reviewed = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(reviewed.Content.AccessGrant.CircleGrants.Exists(
            g => g.CircleId == SystemCircleConstants.ReviewedConnectionsCircleId), "precondition");

        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(unreview.IsSuccessStatusCode, $"un-review failed: {unreview.Error?.Content}");

        // What the review granted, the un-review takes back -- otherwise the stamp is gone while the
        // keys that membership carries are still held.
        var after = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNull(after.Content.ReviewedAt);
        ClassicAssert.IsFalse(after.Content.AccessGrant.CircleGrants.Exists(
            g => g.CircleId == SystemCircleConstants.ReviewedConnectionsCircleId),
            "un-review must revoke the Reviewed circle grant");

        await Cleanup();
    }

    [Test]
    public async Task ReviewStampsAndEnrollsChosenCirclesAtomically()
    {
        var (frodo, sam) = await Connect();

        var circleId = Guid.NewGuid();
        var created = await frodo.Network.CreateCircle(circleId, "Friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });
        ClassicAssert.IsTrue(created.IsSuccessStatusCode);

        await frodo.Network.UnreviewConnection(sam.OdinId);

        var review = await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(info.Content.ReviewedAt, "the review must stamp");
        ClassicAssert.IsTrue(info.Content.AccessGrant.CircleGrants.Exists(g => g.CircleId == circleId),
            "the review must enroll the chosen circle");

        await Cleanup();
    }

    [Test]
    public async Task ReviewIsIdempotentForCirclesAlreadyHeld()
    {
        var (frodo, sam) = await Connect();

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Family", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var first = await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);
        ClassicAssert.IsTrue(first.IsSuccessStatusCode);

        // Re-running the same review must not blow up on "already a member".
        var second = await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);
        ClassicAssert.IsTrue(second.IsSuccessStatusCode, $"second review failed: {second.Error?.Content}");

        await Cleanup();
    }

    [Test]
    public async Task UnreviewIsRejectedWhileTheContactHoldsACircle()
    {
        var (frodo, sam) = await Connect();

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Beer Drinking Buddies", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);

        // Circle ACLs check membership, not tier, so membership must imply review.
        var unreview = await frodo.Network.UnreviewConnection(sam.OdinId);
        ClassicAssert.IsFalse(unreview.IsSuccessStatusCode, "un-review must be rejected for a circle member");
        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, unreview.StatusCode);

        var stillReviewed = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(stillReviewed.Content.ReviewedAt, "the stamp must survive a rejected un-review");

        await Cleanup();
    }

    [Test]
    public async Task RemovingTheLastCircleLeavesTheContactReviewed()
    {
        var (frodo, sam) = await Connect();

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Book Club", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);
        await frodo.Network.RevokeCircle(circleId, sam.OdinId);

        var info = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(info.Content.ReviewedAt,
            "removing the last circle drops the contact to Chat, not back to New");

        await Cleanup();
    }

    [Test]
    public async Task IntroducedConnectionIsNotStampedReviewed()
    {
        // The negative half of OwnerAcceptedConnectionIsStampedReviewed. An introduction forms without
        // the owner present, so it must stay unreviewed until they act -- this is what keeps the legacy
        // Vetted flag exact for auto-connections.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await frodo.Connections.SendConnectionRequest(merry.OdinId, []);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);
        await merry.Connections.AcceptConnectionRequest(frodo.OdinId);

        await frodo.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
        await merry.Connections.DeleteAllIntroductions();

        var introResponse = await frodo.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "meet each other",
            Recipients = [TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId]
        });
        ClassicAssert.IsTrue(introResponse.IsSuccessStatusCode);

        await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await sam.Connections.AwaitIntroductionsProcessing();
        await merry.Connections.AwaitIntroductionsProcessing();

        var samToMerry = await merry.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(samToMerry.IsSuccessStatusCode);
        ClassicAssert.AreEqual(ConnectionStatus.Connected, samToMerry.Content.Status);

        // Sanity: this really is the auto-connect path, not a direct accept.
        ClassicAssert.IsTrue(
            samToMerry.Content.AccessGrant.CircleGrants.Exists(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId),
            "expected the introduced identity to land in the AutoConnections circle");

        ClassicAssert.IsNull(samToMerry.Content.ReviewedAt, "an introduction must not stamp the review");
        ClassicAssert.IsFalse(samToMerry.Content.Vetted, "an unreviewed auto-connection must not read as vetted");

        // ...and the owner reviewing it is what promotes it.
        var review = await merry.Network.ReviewConnection(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var afterReview = await merry.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsNotNull(afterReview.Content.ReviewedAt);

        await FullCleanup();
    }

    [Test]
    public async Task GuestViewerGetsIdentitiesOnlyNotTheOwnersJudgments()
    {
        var (frodo, sam) = await Connect();

        // Frodo reviews Sam, so there is a judgment on the record for a guest to leak.
        await frodo.Network.ReviewConnection(sam.OdinId);

        // Sam logs in to Frodo's guest API holding ReadConnections -- the tier that
        // AllConnectedIdentitiesCanViewConnections grants by default.
        var guestContext = new ConnectedIdentityLoggedInOnGuestApi(
            sam.OdinId,
            new TestPermissionKeyList(PermissionKeys.ReadConnections));

        await guestContext.Initialize(frodo);

        try
        {
            var asGuest = new UniversalCircleNetworkApiClient(frodo.OdinId, guestContext.GetFactory());
            var guestView = await asGuest.GetConnectedIdentitiesOverGet();

            ClassicAssert.IsTrue(guestView.IsSuccessStatusCode,
                $"guest read failed: {guestView.StatusCode} {guestView.Error?.Content}");

            var results = guestView.Content.Results;
            ClassicAssert.IsNotEmpty(results, "the guest should still see the list itself");

            foreach (var row in results)
            {
                ClassicAssert.IsNotNull(row.OdinId, "the identity itself is what a guest may see");
                ClassicAssert.IsNull(row.ReviewedAt, "the review stamp is owner-private");
                ClassicAssert.IsFalse(row.Vetted, "the legacy flag must not leak the stamp either");
                ClassicAssert.IsNull(row.IntroducerOdinId, "who introduced them is owner-private");
                ClassicAssert.IsNull(row.AccessGrant, "what they were granted is owner-private");
                ClassicAssert.IsFalse(row.HasVerificationHash);
            }

            // The owner's own view of the same list is unchanged.
            var ownerView = await frodo.Network.GetConnectionInfo(sam.OdinId);
            ClassicAssert.IsNotNull(ownerView.Content.ReviewedAt, "the owner still sees the stamp");
            ClassicAssert.IsNotNull(ownerView.Content.AccessGrant, "the owner still sees the grants");
        }
        finally
        {
            await guestContext.Cleanup();
        }

        await Cleanup();
    }

    [Test]
    public async Task TheReviewReportsWhatBecameOfEachChosenCircle()
    {
        // The dialog closes when this returns, so the outcome has to travel with the response -- a client
        // that has to re-read status afterwards has already lost the screen it needed to render.
        var (frodo, sam) = await Connect();

        var circleId = Guid.NewGuid();
        await frodo.Network.CreateCircle(circleId, "Friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var review = await frodo.Network.ReviewConnection(sam.OdinId, [circleId]);

        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");
        ClassicAssert.IsNotNull(review.Content, "the review must report its outcome, not an empty body");

        ClassicAssert.Contains(circleId, review.Content.Granted,
            "the owner holds the master key, so the circle is minted rather than deposited");
        ClassicAssert.IsEmpty(review.Content.Deposited);
        ClassicAssert.IsEmpty(review.Content.Pending);
        ClassicAssert.IsNotNull(review.Content.ReviewedAt);

        await Cleanup();
    }

    [Test]
    public async Task AChatOnlyReviewReportsNothingGranted()
    {
        var (frodo, sam) = await Connect();

        var review = await frodo.Network.ReviewConnection(sam.OdinId);

        ClassicAssert.IsTrue(review.IsSuccessStatusCode);
        ClassicAssert.IsEmpty(review.Content.Granted);
        ClassicAssert.IsEmpty(review.Content.Deposited);
        ClassicAssert.IsEmpty(review.Content.Pending);
        ClassicAssert.IsNotNull(review.Content.ReviewedAt, "but the review itself still happened");

        await Cleanup();
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

    private async Task FullCleanup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.DisconnectFrom(sam.Identity.OdinId);
        await frodo.Connections.DisconnectFrom(merry.Identity.OdinId);
        await sam.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await sam.Connections.DisconnectFrom(merry.Identity.OdinId);
        await merry.Connections.DisconnectFrom(frodo.Identity.OdinId);
        await merry.Connections.DisconnectFrom(sam.Identity.OdinId);
    }
}
