#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

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
        Assert.That(forCircle!.Candidates.Select(c => c.OdinId.DomainName), Does.Contain(sam.Identity.DomainName));
        Assert.That(forCircle.GrantOn, Is.EqualTo(CircleGrantOn.Review),
            "the reason they qualify is carried, so a client can name it");

        // The qualifying fact travels with the name: approving access off a bare list is approving
        // on trust, and the date is what lets the owner notice a review they no longer stand behind.
        var samCandidate = forCircle.Candidates.Single(c => c.OdinId.DomainName == sam.Identity.DomainName);
        Assert.That(samCandidate.ReviewedAt, Is.Not.Null, "the review that qualifies them is reported");
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
        Assert.That(before.Single(c => c.CircleId == circleId).Candidates.Select(c => c.OdinId.DomainName),
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

        var result = (await frodo.Admin.GrantCircleToMany(circleId, candidates.Select(c => c.OdinId).ToList()))
            .Content!;

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

        // Named, not just counted: "which one was skipped" is the question an owner asks, and a
        // number cannot answer it.
        Assert.That(result.Outcomes.Count, Is.EqualTo(2));
        Assert.That(result.Outcomes.Count(o => o.Kind == EnrollmentOutcomeKind.Enrolled), Is.EqualTo(1));
        Assert.That(result.Outcomes.Count(o => o.Kind == EnrollmentOutcomeKind.Skipped), Is.EqualTo(1));
        Assert.That(result.Outcomes.All(o => o.OdinId.DomainName == sam.Identity.DomainName), Is.True);
    }

    [Test]
    public async Task TheOwningAppCanSeeAndActOnItsOwnOffer()
    {
        // The app can do this itself, not only the owner console. Doing it in the console is faster
        // -- the master key is there, so grants are minted outright -- but an app doing it records
        // deposits that complete when the connection's Peer Key is next in scope, which is not
        // wrong, just later.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "mailDrive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership, PermissionKeys.ReadConnections });

        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "mail-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: app.AppId, grantOn: CircleGrantOn.Review);

        await ReviewAsync(frodo, sam.Identity);

        // Through the app's own V2 client: app API calls are shared-secret encrypted, so a plain
        // POST body is rejected by the envelope long before any of this logic is reached.
        var appClient = new V2ConnectionNetworkClient(app.Identity, app.Factory);

        var read = await appClient.GetEnrollmentCandidatesAsync(app.AppId);
        Assert.That(read.IsSuccessStatusCode, Is.True,
            $"the owning app must be able to read its own offer, got {read.StatusCode}");
        Assert.That(read.Content!.Single(c => c.CircleId == circleId).Candidates
                .Select(c => c.OdinId.DomainName),
            Does.Contain(sam.Identity.DomainName));

        var write = await appClient.GrantCircleToManyAsync(circleId, [sam.Identity]);
        Assert.That(write.IsSuccessStatusCode, Is.True,
            $"the owning app must be able to act on it, got {write.StatusCode}");

        // Landed as a grant or as a deposit -- which of the two depends on whether the caller could
        // reach the Peer Key, and the point here is only that the app's call did something.
        var members = (await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GetCircleMembersAsync(circleId)).Content!;
        var pending = (await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GetPendingCircleMembersAsync(circleId)).Content!;
        Assert.That(
            members.Any(m => m.DomainName == sam.Identity.DomainName) || pending.Any(),
            Is.True, "the enrolment landed, as membership or as a pending deposit");
    }

    [Test]
    public async Task AnAppCannotAskAboutAnotherAppsCircles()
    {
        // The candidate list is drawn from every connection on the identity, so answering app A's
        // question about app B would tell A which of the owner's contacts were reviewed for a
        // circle that is none of its business.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (otherAppId, _) = await SetupAppOwningAReviewCircleAsync(frodo, "mail");
        await ReviewAsync(frodo, sam.Identity);

        var nosyDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(nosyDrive, "nosyDrive", allowAnonymousReads: false);
        var nosy = await AppSession.SetupAsync(frodo, nosyDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership, PermissionKeys.ReadConnections });

        var read = await new V2ConnectionNetworkClient(nosy.Identity, nosy.Factory)
            .GetEnrollmentCandidatesAsync(otherAppId);

        Assert.That(read.IsSuccessStatusCode, Is.False,
            $"an app asking about another app's circles must be refused, got {read.StatusCode}");
    }

    [Test]
    public async Task AnUnreviewedContactIsOfferedForAConnectCircle()
    {
        // The mirror of AnUnreviewedContactIsNotOfferedForAReviewCircle. GrantOn.Connect is granted
        // "at any connection establishment, ambient introductions included", so connecting is the
        // qualifying act and no review is implied -- which is exactly why the old exclusion keyed on
        // Auto Connections membership was wrong here, and why the rule now reads ReviewedAt only.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // Connect circles are bound by the deposit-only invariant: write/react, never read.
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "chatDrive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "chat-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                        { Drive = drive, Permission = DrivePermission.Write | DrivePermission.React }
                }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: app.AppId, grantOn: CircleGrantOn.Connect);

        // Explicitly unreviewed: connecting stamps a review, so it has to be cleared to get the case.
        var cleared = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .ClearReviewAsync(sam.Identity);
        Assert.That(cleared.IsSuccessStatusCode, Is.True, $"clear review failed: {cleared.StatusCode}");

        var candidates = (await frodo.Admin.GetEnrollmentCandidates(app.AppId)).Content!;
        var forCircle = candidates.SingleOrDefault(c => c.CircleId == circleId);

        Assert.That(forCircle, Is.Not.Null, "a Connect circle offers unreviewed connections");
        Assert.That(forCircle!.Candidates.Select(c => c.OdinId.DomainName),
            Does.Contain(sam.Identity.DomainName));
        Assert.That(forCircle.Candidates.Single(c => c.OdinId.DomainName == sam.Identity.DomainName).ReviewedAt,
            Is.Null, "no review is claimed, because none happened");
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
