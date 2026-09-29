#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/ConnectionReviewTests</c>. Covers
/// <c>Connections.ReviewedAt</c>: when it is stamped, when it must stay null, and that it survives
/// the writes that rewrite the connection row around it. docs/connection-defaults.md, "On verify".
/// </summary>
/// <remarks>
/// <para>
/// <c>MarkReviewed</c> / <c>ClearReview</c> are the system under test and are not on
/// <see cref="ConnectionsHandle"/>, so they go through
/// <see cref="OwnerSession.RefitFor{T}"/> on <see cref="IRefitUniversalCircleNetworkConnections"/> —
/// the same interface the original's <c>Network</c> client wrapped.
/// </para>
/// <para>
/// The original's <c>Connect</c> helper opened with a <c>Disconnect</c> because the fixture shared
/// identities process-wide and a stale connection would carry the previous test's
/// <c>ReviewedAt</c> forward. Per-test reset removes that hazard, so the leading disconnect and
/// every trailing <c>Disconnect</c> / <c>RevokeCircle</c> that only restored state are dropped. The
/// <c>RevokeCircle</c> calls inside
/// <see cref="ClearingTheReviewIsRejectedWhileHoldingAPersonalCircle"/> are <b>not</b> cleanup —
/// each one changes what the next <c>ClearReview</c> must answer — and are kept.
/// </para>
/// <para>
/// <c>AwaitIntroductionsProcessing()</c> polls the transient-temp-drive outbox and needs the outbox
/// background service, which the fast host registers but never starts; it becomes
/// <c>Sync.DrainOutboxAsync()</c>, which runs the same per-item logic synchronously.
/// </para>
/// <para>
/// No caller matrix in the original and none added, so <c>SetupCallerWithOwner</c> ordering is not
/// in play.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionReviewTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    [Test]
    public async Task AcceptingAConnectionRequestStampsTheReviewOnBothSides()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await Connect(frodo, sam);

        // Sam accepted: the review happening at accept time.
        var samsViewOfFrodo = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(samsViewOfFrodo.IsSuccessStatusCode, Is.True);
        Assert.That(samsViewOfFrodo.Content!.ReviewedAt, Is.Not.Null, "accepting a request must stamp the review");

        // Frodo sent the request himself, naming the circles: the sender's half of the same act.
        var frodosViewOfSam = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(frodosViewOfSam.IsSuccessStatusCode, Is.True);
        Assert.That(frodosViewOfSam.Content!.ReviewedAt, Is.Not.Null, "an owner-sent request is reviewed when it completes");
    }

    [Test]
    public async Task TheReviewSurvivesALaterWriteToTheConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await Connect(frodo, sam);

        var stamped = (await sam.Connections.GetConnectionInfo(frodo.Identity)).Content!.ReviewedAt;
        Assert.That(stamped, Is.Not.Null);

        // Granting a circle rewrites the whole connection row.  The stamp lives in a column that every one
        // of those writes has to carry forward, so this is the regression that catches it being dropped.
        var circleId = Guid.NewGuid();
        var createCircle = await sam.Admin.CreateCircle(circleId, "some circle", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });
        Assert.That(createCircle.IsSuccessStatusCode, Is.True);

        var grant = await sam.Connections.GrantCircle(circleId, frodo.Identity);
        Assert.That(grant.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(after.Content!.ReviewedAt, Is.EqualTo(stamped), "the review was lost by a later write");
    }

    [Test]
    public async Task ReviewingIsIdempotentAndTheStampIsSetOnce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await Connect(frodo, sam);

        var first = (await sam.Connections.GetConnectionInfo(frodo.Identity)).Content!.ReviewedAt;
        Assert.That(first, Is.Not.Null);

        var circleId = Guid.NewGuid();
        var createCircle = await sam.Admin.CreateCircle(circleId, "friends", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.AllowIntroductions)
        });
        Assert.That(createCircle.IsSuccessStatusCode, Is.True);

        // A second review may enroll more circles, but must not move the date the owner first vouched.
        var review = await MarkReviewed(sam, frodo.Identity, [circleId]);
        Assert.That(review.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(after.Content!.ReviewedAt, Is.EqualTo(first), "the stamp is set once and must not move");
        Assert.That(after.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == circleId),
            "the review must enroll the circles it was given");

        // Enrolling a circle the contact already holds is a no-op, not an error.
        var again = await MarkReviewed(sam, frodo.Identity, [circleId]);
        Assert.That(again.IsSuccessStatusCode, Is.True, "enrollment must be idempotent");
    }

    [Test]
    public async Task ClearingTheReviewIsRejectedWhileHoldingAPersonalCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await Connect(frodo, sam);

        // Two, so the rejection has more than one offender to report.
        var familyCircleId = Guid.NewGuid();
        var workCircleId = Guid.NewGuid();

        foreach (var (id, name) in new[] { (familyCircleId, "family"), (workCircleId, "work") })
        {
            var createCircle = await sam.Admin.CreateCircle(id, name, new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.AllowIntroductions)
            });
            Assert.That(createCircle.IsSuccessStatusCode, Is.True, $"create '{name}' failed");
        }

        var review = await MarkReviewed(sam, frodo.Identity, [familyCircleId, workCircleId]);
        Assert.That(review.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // circleIdList ACLs check membership, not tier, so a personal-circle member must stay reviewed.
        var rejected = await ClearReview(sam, frodo.Identity);
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "clearing must be refused while a personal circle is held");
        Assert.That(TestUtils.ParseProblemDetails(rejected.Error!),
            Is.EqualTo(OdinClientErrorCode.CannotClearReviewWhilePersonalCircleMember));

        // Every offender in one response, with ids: the caller has to clear all of them, and finding
        // that out one rejected attempt at a time is the thing this avoids.
        var blocking = ReadBlockingCircles(rejected.Error?.Content);
        Assert.That(blocking, Has.Count.EqualTo(2), "the rejection must name every blocking circle, not just the first");
        Assert.That(blocking.Select(b => Guid.Parse(b!).ToString("N")).ToList(),
            Is.EquivalentTo(new[] { familyCircleId, workCircleId }.Select(g => g.ToString("N")).ToList()));

        Assert.That((await sam.Connections.GetConnectionInfo(frodo.Identity)).Content!.ReviewedAt, Is.Not.Null);

        // One is not enough -- the other still blocks.
        await sam.Connections.RevokeCircle(familyCircleId, frodo.Identity);

        var stillRejected = await ClearReview(sam, frodo.Identity);
        Assert.That(stillRejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ReadBlockingCircles(stillRejected.Error?.Content), Has.Count.EqualTo(1));

        // Remove the last membership and the clear goes through.
        await sam.Connections.RevokeCircle(workCircleId, frodo.Identity);

        var cleared = await ClearReview(sam, frodo.Identity);
        Assert.That(cleared.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await sam.Connections.GetConnectionInfo(frodo.Identity);
        Assert.That(after.Content!.ReviewedAt, Is.Null, "clearing returns the connection to New");
    }

    /// <summary>The circle ids the rejection named, read off the problem-details body.</summary>
    private static List<string?> ReadBlockingCircles(string? problemDetails)
    {
        Assert.That(problemDetails, Is.Not.Null, "the rejection had no body to read");

        using var doc = JsonDocument.Parse(problemDetails!);
        Assert.That(doc.RootElement.TryGetProperty("blockingCircles", out var circles), Is.True,
            $"the rejection carried no blockingCircles: {problemDetails}");

        return circles.EnumerateArray()
            .Select(c => c.GetProperty("circleId").GetString())
            .ToList();
    }

    [Test]
    public async Task AnAutoConnectionIsNotReviewedUntilTheOwnerReviewsIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        // Frodo knows both; Sam and Merry do not know each other.
        await Connect(frodo, sam);
        await Connect(frodo, merry);

        await Requests(frodo).DeleteAllIntroductions();
        await Requests(sam).DeleteAllIntroductions();
        await Requests(merry).DeleteAllIntroductions();

        var introductions = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "you two should meet",
            Recipients = [sam.Identity, merry.Identity]
        });
        Assert.That(introductions.IsSuccessStatusCode, Is.True);
        await frodo.Sync.DrainOutboxAsync();

        await merry.Sync.DrainOutboxAsync();
        await sam.Sync.DrainOutboxAsync();

        // Nobody reviewed this one -- it is New, and stays New until Merry looks at it.
        var beforeReview = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(beforeReview.IsSuccessStatusCode, Is.True);
        Assert.That(beforeReview.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
        Assert.That(beforeReview.Content.ReviewedAt, Is.Null, "an auto-connection must not be reviewed");

        // The review with every toggle declined: it still stamps, and takes nothing away.
        var review = await MarkReviewed(merry, sam.Identity);
        Assert.That(review.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var afterReview = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(afterReview.Content!.ReviewedAt, Is.Not.Null);
        Assert.That(afterReview.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId),
            "the review removes nothing the connection already held");
    }

    private static async Task Connect(OwnerSession a, OwnerSession b)
    {
        await a.Connections.SendConnectionRequest(b.Identity, []);
        await b.Connections.AcceptConnectionRequest(a.Identity);
    }

    private static Task<ApiResponse<HttpContent>> MarkReviewed(
        OwnerSession owner, OdinId recipient, IEnumerable<GuidId>? circleIds = null) =>
        owner.RefitFor<IRefitUniversalCircleNetworkConnections>().MarkReviewed(new MarkConnectionReviewedRequest
        {
            OdinId = recipient,
            CircleIds = circleIds ?? []
        });

    private static Task<ApiResponse<HttpContent>> ClearReview(OwnerSession owner, OdinId recipient) =>
        owner.RefitFor<IRefitUniversalCircleNetworkConnections>().ClearReview(new OdinIdRequest { OdinId = recipient });
}
