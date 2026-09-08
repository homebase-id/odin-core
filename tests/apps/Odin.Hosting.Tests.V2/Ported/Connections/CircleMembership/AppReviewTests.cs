#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
/// The connection review performed by an app rather than the owner -- the combination the product
/// actually ships, since the review dialog runs inside an app and never holds the master key.
/// </summary>
/// <remarks>
/// The stamp lands immediately either way; what differs is the circles.  A read-bearing circle is
/// deposited rather than minted, so an app review leaves the contact <i>reviewed now, circles pending</i>
/// -- a state a client has to be able to explain.
/// </remarks>
[TestFixture]
public class AppReviewTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppReview_StampsImmediately_ButLeavesAReadCirclePending()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);

        // The accept already reviewed this connection; clear it so the app's review is the one under
        // test rather than a no-op on an already-stamped record.
        var cleared = await owner.ClearReviewAsync(sam.Identity);
        Assert.That(cleared.IsSuccessStatusCode, Is.True, $"clear failed: {cleared.StatusCode}");
        Assert.That((await owner.GetConnectionInfoAsync(sam.Identity)).Content!.ReviewedAt, Is.Null,
            "precondition: the connection should be back to New");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"app review failed: {review.StatusCode}");

        var info = await owner.GetConnectionInfoAsync(sam.Identity);
        Assert.That(info.Content!.ReviewedAt, Is.Not.Null,
            "the stamp needs no key, so an app review records it straight away");

        // ...but the circle it chose does need one, so it is pending rather than granted.
        var icr = await Host.GetTenantScope(frodo.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.False,
            "a read-bearing circle cannot be minted by an app");
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle), Is.True,
            "it should be deposited, awaiting conversion");

        Assert.That(info.Content.AccessGrant.PendingCircleIds.Contains(circle), Is.True,
            "and reported as pending so a client can say so");
    }

    [Test]
    public async Task AppReview_WithAWriteOnlyCircle_IsLiveImmediately()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "chatDrive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "chat-shaped", new PermissionSetGrantRequest
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
        });

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write | DrivePermission.React,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"app review failed: {review.StatusCode}");

        // No key material in a write-only grant, so nothing is deferred: reviewed and enrolled at once.
        Assert.That((await owner.GetConnectionInfoAsync(sam.Identity)).Content!.ReviewedAt, Is.Not.Null);
        Assert.That((await owner.GetCircleMembersAsync(circle)).Content!.Any(m => m == sam.Identity), Is.True,
            "a write-only circle chosen in an app review takes effect immediately");
    }

    [Test]
    public async Task AppReview_FailingPartWayThrough_LeavesNothingBehind()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, goodCircle, app) = await SetupAppWithReadCircleAsync(frodo);

        // A second circle on a drive the app has nothing on: the review enrols circle by circle, so this
        // one fails after the first has already been written.
        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "otherDrive", allowAnonymousReads: false);

        var outOfScopeCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(outOfScopeCircle, "out-of-scope", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = otherDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [goodCircle, outOfScopeCircle]);
        Assert.That(review.IsSuccessStatusCode, Is.False, "the review must fail if any circle cannot be enrolled");

        // One transaction: the circle that did succeed is rolled back, and so is the stamp.
        var icr = await Host.GetTenantScope(frodo.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == goodCircle), Is.False,
            "the first circle should have been rolled back with the rest");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(outOfScopeCircle), Is.False);
        Assert.That(icr.ReviewedAt, Is.Null,
            "a review that did not happen must not leave the contact marked reviewed");
    }

    private static async Task<(TargetDrive drive, Guid circle, AppSession app)> SetupAppWithReadCircleAsync(
        OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "readDrive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "read-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        return (drive, circle, app);
    }
}
