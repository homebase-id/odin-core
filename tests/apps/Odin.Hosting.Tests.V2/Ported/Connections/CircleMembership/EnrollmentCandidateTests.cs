#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// The backlog an app inherits when it is handed a circle: contacts the owner already reviewed who
/// were never offered that circle, because at review time it did not exist or was not the app's.
/// </summary>
/// <remarks>
/// A review is a moment, not a standing rule, so nothing enrols them retroactively and nothing says
/// they are waiting.  These pin who qualifies -- the question the offer on the app's page rests on --
/// and that the bulk add re-decides it rather than trusting the list it is given.
/// </remarks>
[TestFixture]
public class EnrollmentCandidateTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task AReviewedContactIsOfferedForAReviewCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (appId, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);

        var candidates = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!;

        var forCircle = candidates.SingleOrDefault(c => c.CircleId == circleId);
        Assert.That(forCircle, Is.Not.Null, "the app's review circle should have an offer");
        Assert.That(forCircle!.Candidates.Select(c => c.DomainName), Does.Contain(sam.Identity.DomainName));
        Assert.That(forCircle.GrantOn, Is.EqualTo(CircleGrantOn.Review),
            "the reason they qualify is carried, so a client can name it");
    }

    [Test]
    public async Task AnUnreviewedContactIsNotOfferedForAReviewCircle()
    {
        // GrantOn.Review says the review is the qualifying event. Without one there is nothing to
        // act on -- offering them would be inventing the owner's decision rather than carrying it.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (appId, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");

        // Connecting stamps the review on both sides (accepting a request *is* the review), so an
        // unreviewed connected contact has to be made deliberately.
        var cleared = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .ClearReviewAsync(sam.Identity);
        Assert.That(cleared.IsSuccessStatusCode, Is.True, $"clear review failed: {cleared.StatusCode}");

        var candidates = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!;
        var forCircle = candidates.SingleOrDefault(c => c.CircleId == circleId);

        Assert.That(forCircle, Is.Null, "an unreviewed contact is not a candidate, so the circle is omitted");
    }

    [Test]
    public async Task AManualCircleOffersNobody()
    {
        // GrantOn.None is manual membership by definition; there is no rule to apply in bulk.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (appId, circleId) = await SetupAppOwningACircleAsync(frodo, "mail", CircleGrantOn.None);
        await ReviewAsync(frodo, sam.Identity);

        var candidates = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!;

        Assert.That(candidates.Any(c => c.CircleId == circleId), Is.False);
    }

    [Test]
    public async Task AnExistingMemberIsNotOfferedAgain()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (appId, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);

        var before = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!;
        Assert.That(before.Single(c => c.CircleId == circleId).Candidates.Select(c => c.DomainName),
            Does.Contain(sam.Identity.DomainName));

        var result = (await frodo.Admin.GrantCircleToMany(circleId, [sam.Identity])).Content!;
        Assert.That(result.Enrolled, Is.EqualTo(1));

        var after = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!;
        Assert.That(after.Any(c => c.CircleId == circleId), Is.False,
            "once they are a member there is nothing left to offer");
    }

    [Test]
    public async Task BulkAddEnrolsEveryoneEligibleAndReportsIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");
        await PeerFlow.CreatePeerDriveAsync(frodo, merry, DrivePermission.Read, "baseline2");

        var (appId, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);
        await ReviewAsync(frodo, merry.Identity);

        var candidates = (await frodo.Admin.GetEnrollmentCandidates(appId)).Content!
            .Single(c => c.CircleId == circleId).Candidates;
        Assert.That(candidates.Count, Is.EqualTo(2));

        var result = (await frodo.Admin.GrantCircleToMany(circleId, candidates)).Content!;

        Assert.That(result.Enrolled, Is.EqualTo(2), "the owner holds the master key, so these are grants outright");
        Assert.That(result.Deposited, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));

        var members = (await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GetCircleMembersAsync(circleId)).Content!;
        Assert.That(members.Select(m => m.DomainName),
            Is.SupersetOf(new[] { sam.Identity.DomainName, merry.Identity.DomainName }));
    }

    [Test]
    public async Task BulkAddSkipsSomeoneWhoNoLongerQualifies()
    {
        // The list comes from a view that may be seconds old, so eligibility is re-decided per
        // identity rather than trusted. Here the same identity is submitted twice in one call: the
        // second pass finds them already a member.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);

        var result = (await frodo.Admin.GrantCircleToMany(circleId, [sam.Identity, sam.Identity])).Content!;

        Assert.That(result.Enrolled, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(1), "the repeat is skipped, not an error for the batch");
    }

    [Test]
    public async Task AnAppCannotSeeOrActOnTheOffer()
    {
        // Owner console only, and pinned at the route rather than left to the service's master-key
        // assertion. These live on OwnerCircleNetworkController and not on the shared base that
        // AppCircleNetworkController inherits, so an app finds nothing there at all -- which says
        // "never yours to call" where a 403 would say "you lack permission".
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (appId, circleId) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);

        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "callerDrive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var client = app.Factory.CreateHttpClient(app.Identity, out _);

        var read = await client.GetAsync($"/api/apps/v1/circles/connections/enrollment-candidates?appId={appId}");
        Assert.That(read.IsSuccessStatusCode, Is.False,
            $"an app must not be able to read the offer, got {read.StatusCode}");

        var write = await client.PostAsync("/api/apps/v1/circles/connections/circles/add-many",
            new StringContent($"{{\"circleId\":\"{circleId}\",\"odinIds\":[\"{sam.Identity.DomainName}\"]}}",
                Encoding.UTF8, "application/json"));
        Assert.That(write.IsSuccessStatusCode, Is.False,
            $"an app must not be able to act on the offer, got {write.StatusCode}");
    }

    //

    private static async Task<(Guid appId, Guid circleId)> SetupAppOwningAReviewCircleAsync(
        OwnerSession owner, string label) =>
        await SetupAppOwningACircleAsync(owner, label, CircleGrantOn.Review);

    private static async Task<(Guid appId, Guid circleId)> SetupAppOwningACircleAsync(
        OwnerSession owner, string label, CircleGrantOn grantOn)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, $"{label}Drive", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, $"{label}-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: app.AppId, grantOn: grantOn);

        return (app.AppId, circleId);
    }

    private static async Task ReviewAsync(OwnerSession owner, OdinId target)
    {
        var review = await new V2ConnectionNetworkClient(owner.Identity, owner.Factory)
            .MarkReviewedAsync(target);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
    }
}
