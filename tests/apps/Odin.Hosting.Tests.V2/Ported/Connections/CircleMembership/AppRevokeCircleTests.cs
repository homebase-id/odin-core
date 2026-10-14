#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// An app may remove a connection from a circle it owns; from any other only if it holds
/// <c>ManageCircleMembership</c>.
/// </summary>
/// <remarks>
/// Owning the circle is the app's authority.  The apps that succeed on their own circles hold no permission
/// keys, so ownership alone lets them in.  Another app's circle and the owner's own are refused to an app
/// without the key; one that holds it (as Chat does by default) may.
/// </remarks>
[TestFixture]
public class AppRevokeCircleTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly int[] HoldsManageCircleMembership = [PermissionKeys.ManageCircleMembership];

    [Test]
    public async Task AppRemovesAMember_FromItsOwnCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (app, appCircle) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Write | DrivePermission.React);
        await GrantAsOwnerAsync(frodo, appCircle, sam.Identity);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .RevokeCircleAsync(appCircle, sam.Identity);

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"an app must be able to remove members from its own circle; got {response.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(appCircle), Is.False);
    }

    [Test]
    public async Task AppRemovingAMember_AlsoDropsTheirQueuedEnrollment()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (app, appCircle) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Read);
        await EnqueueViaReviewAsync(frodo, sam.Identity, appCircle);

        var before = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(before.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == appCircle), Is.True,
            "precondition: the review should have queued the circle for its owning app");

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .RevokeCircleAsync(appCircle, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"revoke failed: {response.StatusCode}");

        // Left queued, the entry would put them back the next time the app processed its enrollments.
        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == appCircle), Is.False);

        var processed = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(processed.IsSuccessStatusCode, Is.True, $"process failed: {processed.StatusCode}");
        Assert.That(processed.Content!.EnrollmentsCompleted, Is.EqualTo(0));
    }

    [TestCase(false, HttpStatusCode.Forbidden, TestName = "AppCannotRemoveAMember_FromAnotherAppsCircle")]
    [TestCase(true, HttpStatusCode.OK, TestName = "AppHoldingTheRetiredKey_CanStillRemoveAMember_FromAnotherAppsCircle")]
    public async Task AppRemovingAMember_FromAnotherAppsCircle(bool holdsKey, HttpStatusCode expected)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, otherAppsCircle) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Write | DrivePermission.React);
        var (app, _) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Write | DrivePermission.React,
            holdsKey ? HoldsManageCircleMembership : null);
        await GrantAsOwnerAsync(frodo, otherAppsCircle, sam.Identity);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .RevokeCircleAsync(otherAppsCircle, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(expected), $"got {response.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(otherAppsCircle), Is.EqualTo(!holdsKey),
            holdsKey ? "the key still lets the app remove the member" : "the refused revoke must leave the membership as it was");
    }

    [TestCase(false, HttpStatusCode.Forbidden, TestName = "AppCannotRemoveAMember_FromAnOwnerCircle")]
    [TestCase(true, HttpStatusCode.OK, TestName = "AppHoldingTheRetiredKey_CanStillRemoveAMember_FromAnOwnerCircle")]
    public async Task AppRemovingAMember_FromAnOwnerCircle(bool holdsKey, HttpStatusCode expected)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (app, _) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Write | DrivePermission.React,
            holdsKey ? HoldsManageCircleMembership : null);

        // No owning app: the owner console's own circle.
        var ownerDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(ownerDrive, "ownerDrive", allowAnonymousReads: false);
        var ownerCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(ownerCircle, "owner-owned",
            GrantOn(ownerDrive, DrivePermission.Write | DrivePermission.React));
        await GrantAsOwnerAsync(frodo, ownerCircle, sam.Identity);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .RevokeCircleAsync(ownerCircle, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(expected), $"got {response.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(ownerCircle), Is.EqualTo(!holdsKey),
            holdsKey ? "the key still lets the app remove the member" : "the refused revoke must leave the membership as it was");
    }

    [Test]
    public async Task AppRemovingAMember_FromACircleThatDoesNotExist_IsNotFound()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (app, _) = await SetupAppOwningACircleAsync(frodo, DrivePermission.Write | DrivePermission.React);

        // An app cannot show it owns a circle that is not there, so it is refused rather than
        // allowed through to clean up whatever grants may still name the id.
        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .RevokeCircleAsync(Guid.NewGuid(), sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            $"expected a client error; got {response.StatusCode}");
        Assert.That(TestUtils.ParseProblemDetails(response.Error!), Is.EqualTo(OdinClientErrorCode.CircleNotFound));
    }

    /// <summary>
    /// An app holding the given permission on a drive of its own, and a circle that app owns granting
    /// the same permission on that drive.  The app has no permission keys unless given some.
    /// </summary>
    private static async Task<(AppSession app, Guid circle)> SetupAppOwningACircleAsync(
        OwnerSession owner, DrivePermission permission, IReadOnlyList<int>? permissionKeys = null)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, $"appDrive-{Guid.NewGuid():N}", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(owner, drive, permission, permissionKeys: permissionKeys ?? Array.Empty<int>());

        var circle = Guid.NewGuid();
        await owner.Admin.CreateCircle(circle, $"app-owned-{Guid.NewGuid():N}", GrantOn(drive, permission),
            appId: app.AppId);

        return (app, circle);
    }

    private static PermissionSetGrantRequest GrantOn(TargetDrive drive, DrivePermission permission) => new()
    {
        Drives = new List<DriveGrantRequest>
        {
            new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } }
        },
        PermissionSet = new PermissionSet(new List<int>())
    };

    private static async Task GrantAsOwnerAsync(OwnerSession owner, Guid circleId, OdinId member)
    {
        var grant = await new V2ConnectionNetworkClient(owner.Identity, owner.Factory)
            .GrantCircleAsync(circleId, member);
        Assert.That(grant.IsSuccessStatusCode, Is.True, $"precondition: owner grant failed: {grant.StatusCode}");
    }

    /// <summary>
    /// Queues the circle for its owning app the way a real review does: from a second app that cannot
    /// reach the circle's drives, and so can only record the owner's choice.
    /// </summary>
    private static async Task EnqueueViaReviewAsync(OwnerSession owner, OdinId target, Guid circleId)
    {
        var reviewerDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(reviewerDrive, $"reviewerDrive-{Guid.NewGuid():N}", allowAnonymousReads: false);
        var reviewer = await AppSession.SetupAsync(owner, reviewerDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var review = await new V2ConnectionNetworkClient(reviewer.Identity, reviewer.Factory)
            .MarkReviewedAsync(target, [circleId]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr, Is.Not.Null, $"no connection record for {target}");
        return icr!;
    }
}
