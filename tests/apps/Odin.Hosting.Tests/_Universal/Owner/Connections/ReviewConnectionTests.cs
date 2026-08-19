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
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
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

        // No circles == the "chat only" outcome: it records the decision and grants nothing.
        var review = await frodo.Network.ReviewConnection(sam.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.Error?.Content}");

        var reviewed = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsNotNull(reviewed.Content.ReviewedAt);
        ClassicAssert.IsTrue(reviewed.Content.Vetted);
        ClassicAssert.AreEqual(grantCountBefore, reviewed.Content.AccessGrant.CircleGrants.Count,
            "a chat-only review must not mint any circle grant");

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
