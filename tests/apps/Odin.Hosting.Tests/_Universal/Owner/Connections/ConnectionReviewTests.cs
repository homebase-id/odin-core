using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections;

/// <summary>
/// Covers <c>Connections.ReviewedAt</c>: when it is stamped, when it must stay null, and that it survives
/// the writes that rewrite the connection row around it.  docs/connection-defaults.md, "On verify".
/// </summary>
public class ConnectionReviewTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
            new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Merry, TestIdentities.Samwise });
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
    public async Task AcceptingAConnectionRequestStampsTheReviewOnBothSides()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        // Sam accepted: the review happening at accept time.
        var samsViewOfFrodo = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsTrue(samsViewOfFrodo.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(samsViewOfFrodo.Content.ReviewedAt, "accepting a request must stamp the review");

        // Frodo sent the request himself, naming the circles: the sender's half of the same act.
        var frodosViewOfSam = await frodo.Network.GetConnectionInfo(sam.OdinId);
        ClassicAssert.IsTrue(frodosViewOfSam.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(frodosViewOfSam.Content.ReviewedAt, "an owner-sent request is reviewed when it completes");

        await Disconnect(frodo, sam);
    }

    [Test]
    public async Task TheReviewSurvivesALaterWriteToTheConnection()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var stamped = (await sam.Network.GetConnectionInfo(frodo.OdinId)).Content.ReviewedAt;
        ClassicAssert.IsNotNull(stamped);

        // Granting a circle rewrites the whole connection row.  The stamp lives in a column that every one
        // of those writes has to carry forward, so this is the regression that catches it being dropped.
        var circleId = Guid.NewGuid();
        var createCircle = await sam.Network.CreateCircle(circleId, "some circle", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });
        ClassicAssert.IsTrue(createCircle.IsSuccessStatusCode);

        var grant = await sam.Network.GrantCircle(circleId, frodo.OdinId);
        ClassicAssert.IsTrue(grant.IsSuccessStatusCode, $"grant failed: {grant.StatusCode}");

        var after = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.AreEqual(stamped, after.Content.ReviewedAt, "the review was lost by a later write");

        await sam.Network.RevokeCircle(circleId, frodo.OdinId);
        await Disconnect(frodo, sam);
    }

    [Test]
    public async Task ReviewingIsIdempotentAndTheStampIsSetOnce()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        var first = (await sam.Network.GetConnectionInfo(frodo.OdinId)).Content.ReviewedAt;
        ClassicAssert.IsNotNull(first);

        var circleId = Guid.NewGuid();
        var createCircle = await sam.Network.CreateCircle(circleId, "friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.AllowIntroductions)
        });
        ClassicAssert.IsTrue(createCircle.IsSuccessStatusCode);

        // A second review may enroll more circles, but must not move the date the owner first vouched.
        var review = await sam.Network.MarkReviewed(frodo.OdinId, [circleId]);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.StatusCode}");

        var after = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.AreEqual(first, after.Content.ReviewedAt, "the stamp is set once and must not move");
        ClassicAssert.IsTrue(after.Content.AccessGrant.CircleGrants.Exists(cg => cg.CircleId == circleId),
            "the review must enroll the circles it was given");

        // Enrolling a circle the contact already holds is a no-op, not an error.
        var again = await sam.Network.MarkReviewed(frodo.OdinId, [circleId]);
        ClassicAssert.IsTrue(again.IsSuccessStatusCode, "enrollment must be idempotent");

        await sam.Network.RevokeCircle(circleId, frodo.OdinId);
        await Disconnect(frodo, sam);
    }

    [Test]
    public async Task ClearingTheReviewIsRejectedWhileHoldingAPersonalCircle()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await Connect(frodo, sam);

        // Two, so the rejection has more than one offender to report.
        var familyCircleId = Guid.NewGuid();
        var workCircleId = Guid.NewGuid();

        foreach (var (id, name) in new[] { (familyCircleId, "family"), (workCircleId, "work") })
        {
            var createCircle = await sam.Network.CreateCircle(id, name, new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.AllowIntroductions)
            });
            ClassicAssert.IsTrue(createCircle.IsSuccessStatusCode, $"create '{name}' failed");
        }

        var review = await sam.Network.MarkReviewed(frodo.OdinId, [familyCircleId, workCircleId]);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.StatusCode}");

        // circleIdList ACLs check membership, not tier, so a personal-circle member must stay reviewed.
        var rejected = await sam.Network.ClearReview(frodo.OdinId);
        ClassicAssert.IsFalse(rejected.IsSuccessStatusCode, "clearing must be refused while a personal circle is held");
        ClassicAssert.AreEqual(OdinClientErrorCode.CannotClearReviewWhilePersonalCircleMember,
            WebScaffold.GetErrorCode(rejected.Error));

        // Every offender in one response, with ids: the caller has to clear all of them, and finding
        // that out one rejected attempt at a time is the thing this avoids.
        var blocking = ReadBlockingCircles(rejected.Error?.Content);
        ClassicAssert.AreEqual(2, blocking.Count, "the rejection must name every blocking circle, not just the first");
        CollectionAssert.AreEquivalent(
            new[] { familyCircleId, workCircleId }.Select(g => g.ToString("N")).ToList(),
            blocking.Select(b => Guid.Parse(b).ToString("N")).ToList());

        ClassicAssert.IsNotNull((await sam.Network.GetConnectionInfo(frodo.OdinId)).Content.ReviewedAt);

        // One is not enough -- the other still blocks.
        await sam.Network.RevokeCircle(familyCircleId, frodo.OdinId);

        var stillRejected = await sam.Network.ClearReview(frodo.OdinId);
        ClassicAssert.IsFalse(stillRejected.IsSuccessStatusCode);
        ClassicAssert.AreEqual(1, ReadBlockingCircles(stillRejected.Error?.Content).Count);

        // Remove the last membership and the clear goes through.
        await sam.Network.RevokeCircle(workCircleId, frodo.OdinId);

        var cleared = await sam.Network.ClearReview(frodo.OdinId);
        ClassicAssert.IsTrue(cleared.IsSuccessStatusCode, $"clear failed: {cleared.StatusCode}");

        var after = await sam.Network.GetConnectionInfo(frodo.OdinId);
        ClassicAssert.IsNull(after.Content.ReviewedAt, "clearing returns the connection to New");

        await Disconnect(frodo, sam);
    }

    /// <summary>The circle ids the rejection named, read off the problem-details body.</summary>
    private static List<string> ReadBlockingCircles(string problemDetails)
    {
        ClassicAssert.IsNotNull(problemDetails, "the rejection had no body to read");

        using var doc = JsonDocument.Parse(problemDetails);
        ClassicAssert.IsTrue(doc.RootElement.TryGetProperty("blockingCircles", out var circles),
            $"the rejection carried no blockingCircles: {problemDetails}");

        return circles.EnumerateArray()
            .Select(c => c.GetProperty("circleId").GetString())
            .ToList();
    }

    [Test]
    public async Task AnAutoConnectionIsNotReviewedUntilTheOwnerReviewsIt()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        // Frodo knows both; Sam and Merry do not know each other.
        await Disconnect(sam, merry);
        await Connect(frodo, sam);
        await Connect(frodo, merry);

        await frodo.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
        await merry.Connections.DeleteAllIntroductions();

        var introductions = await frodo.Connections.SendIntroductions(new IntroductionGroup
        {
            Message = "you two should meet",
            Recipients = [TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId]
        });
        ClassicAssert.IsTrue(introductions.IsSuccessStatusCode);
        await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        await merry.Connections.AwaitIntroductionsProcessing();
        await sam.Connections.AwaitIntroductionsProcessing();

        // Nobody reviewed this one -- it is New, and stays New until Merry looks at it.
        var beforeReview = await merry.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(beforeReview.IsSuccessStatusCode);
        ClassicAssert.IsTrue(beforeReview.Content.Status == ConnectionStatus.Connected);
        ClassicAssert.IsNull(beforeReview.Content.ReviewedAt, "an auto-connection must not be reviewed");

        // The review with every toggle declined: it still stamps, and takes nothing away.
        var review = await merry.Network.MarkReviewed(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsTrue(review.IsSuccessStatusCode, $"review failed: {review.StatusCode}");

        var afterReview = await merry.Network.GetConnectionInfo(TestIdentities.Samwise.OdinId);
        ClassicAssert.IsNotNull(afterReview.Content.ReviewedAt);
        ClassicAssert.IsTrue(afterReview.Content.AccessGrant.CircleGrants.Exists(
            cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId),
            "the review removes nothing the connection already held");

        await frodo.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
        await merry.Connections.DeleteAllIntroductions();

        await Disconnect(frodo, sam);
        await Disconnect(frodo, merry);
        await Disconnect(sam, merry);
    }

    /// <summary>
    /// Connects two identities from a clean slate.  Tests in this fixture share identities, so a test that
    /// fails before its cleanup must not leave the next one connected -- and a stale connection would
    /// silently carry the previous test's ReviewedAt into the next assertion.
    /// </summary>
    private static async Task Connect(OwnerApiClientRedux a, OwnerApiClientRedux b)
    {
        await Disconnect(a, b);

        await a.Connections.SendConnectionRequest(b.OdinId, []);
        await b.Connections.AcceptConnectionRequest(a.OdinId);
    }

    private static async Task Disconnect(OwnerApiClientRedux a, OwnerApiClientRedux b)
    {
        await a.Connections.DisconnectFrom(b.Identity.OdinId);
        await b.Connections.DisconnectFrom(a.Identity.OdinId);
    }
}
