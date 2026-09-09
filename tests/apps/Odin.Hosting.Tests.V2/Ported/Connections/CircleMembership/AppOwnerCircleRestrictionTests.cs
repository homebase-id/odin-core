#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
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
/// An owner circle -- one with no owning app -- is out of bounds for an app, both to grant and to see.
/// </summary>
/// <remarks>
/// No app can complete an enrolment into a circle no app owns, so allowing one to try would leave the
/// contact waiting until the owner next opened their console. Rather than queue something only the owner
/// console can finish, the grant is refused and the circle is kept out of what an app is shown, so that
/// what a client can offer and what it can carry out are the same set.
/// </remarks>
[TestFixture]
public class AppOwnerCircleRestrictionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppCannotGrantAnOwnerCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (app, _, ownerCircle) = await SetupAppAndCirclesAsync(frodo);

        // Through the review: GrantCircleAsync is the older path and keeps the behaviour it had, so the
        // restriction lives on enrolment only.
        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [ownerCircle]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"an app must not enrol anyone in a circle it cannot complete; got {response.StatusCode}");

        // Refused, not deferred: queuing it would be the "waiting on the owner console" state this rule
        // exists to prevent.
        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(ownerCircle), Is.False);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == ownerCircle), Is.False,
            "nothing should have been queued for the owner to finish");
    }

    [Test]
    public async Task AppReviewChoosingAnOwnerCircle_IsRefusedWhole()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (app, appCircle, ownerCircle) = await SetupAppAndCirclesAsync(frodo);

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [appCircle, ownerCircle]);

        Assert.That(review.IsSuccessStatusCode, Is.False, "the review must not succeed on a circle no app can grant");

        // All or nothing, as the review has always been: the app circle it could have granted is rolled
        // back with the rest, and the contact is not left marked reviewed on a review that failed.
        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(appCircle), Is.False);
        Assert.That(icr.ReviewedAt, Is.Null);
    }

    [Test]
    public async Task OwnerConsoleCanStillGrantAnOwnerCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, _, ownerCircle) = await SetupAppAndCirclesAsync(frodo);

        // The restriction is about apps, not about the circle: the owner console is scoped to no app and
        // is exactly who these circles are for.
        var response = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GrantCircleAsync(ownerCircle, sam.Identity);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"the owner must still be able to; got {response.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(ownerCircle), Is.True);
    }

    [Test]
    public async Task AppListingCircles_SeesOnlyAppOwnedOnes()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (app, appCircle, ownerCircle) = await SetupAppAndCirclesAsync(frodo);

        var listed = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .GetCirclesWithMembersAsync(includeSystemCircle: false);
        Assert.That(listed.IsSuccessStatusCode, Is.True, $"listing failed: {listed.StatusCode}");

        var ids = listed.Content!.Select(c => c.Circle.Id.Value).ToList();

        Assert.That(ids, Does.Contain(appCircle), "an app sees circles owned by an app");
        Assert.That(ids, Does.Not.Contain(ownerCircle),
            "and not the owner's own, which it could not act on anyway");
        Assert.That(listed.Content.All(c => c.Circle.AppId.HasValue), Is.True,
            "nothing without an owning app should be offered to an app");
    }

    [Test]
    public async Task OwnerConsoleListingCircles_StillSeesItsOwn()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (_, appCircle, ownerCircle) = await SetupAppAndCirclesAsync(frodo);

        var listed = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GetCirclesWithMembersAsync(includeSystemCircle: false);

        var ids = listed.Content!.Select(c => c.Circle.Id.Value).ToList();

        Assert.That(ids, Does.Contain(ownerCircle), "the filter is about apps, not about the circles");
        Assert.That(ids, Does.Contain(appCircle));
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession frodo, OwnerSession sam)
    {
        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr, Is.Not.Null);
        return icr!;
    }

    /// <summary>An app, a circle that app owns, and a circle nobody's app owns.</summary>
    private static async Task<(AppSession app, Guid appCircle, Guid ownerCircle)> SetupAppAndCirclesAsync(
        OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "appDrive", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write | DrivePermission.React,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var grant = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                }
            },
            PermissionSet = new PermissionSet(new List<int>())
        };

        var appCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(appCircle, "app-owned", grant, appId: app.AppId);

        // Same grants, no owning app -- so the only difference under test is ownership.
        var ownerCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(ownerCircle, "owner-owned", grant);

        return (app, appCircle, ownerCircle);
    }
}
