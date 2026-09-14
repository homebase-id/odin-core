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

        // App-owned: an app cannot enrol anyone into a circle that belongs to no app.
        var appId = Guid.NewGuid();
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
        }, appId: appId);

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write | DrivePermission.React,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership }, knownAppId: appId);

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"app review failed: {review.StatusCode}");

        // No key material in a write-only grant, so nothing is deferred: reviewed and enrolled at once.
        Assert.That((await owner.GetConnectionInfoAsync(sam.Identity)).Content!.ReviewedAt, Is.Not.Null);
        Assert.That((await owner.GetCircleMembersAsync(circle)).Content!.Any(m => m == sam.Identity), Is.True,
            "a write-only circle chosen in an app review takes effect immediately");
    }

    [Test]
    public async Task AppReview_WithACircleItCannotGrant_EnqueuesThatOneAndKeepsTheRest()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, reachableCircle, app) = await SetupAppWithReadCircleAsync(frodo);

        // A circle on a drive the reviewing app has nothing on -- the shape of another app's circle
        // ticked from this one's review dialog. The app cannot source its storage key, so it cannot
        // grant it in any form.
        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "otherDrive", allowAnonymousReads: false);

        // Owned by some other app: a circle owned by no app can never be queued, because no app's
        // queue would claim it.
        var outOfReachCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(outOfReachCircle, "out-of-reach", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = otherDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: Guid.NewGuid());

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [reachableCircle, outOfReachCircle]);
        Assert.That(review.IsSuccessStatusCode, Is.True,
            $"the review must not fail because one circle was out of reach: {review.StatusCode}");

        var icr = await Host.GetTenantScope(frodo.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(sam.Identity);

        // What it could do, it did.
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == reachableCircle), Is.True,
            "the circle the app could source keys for should have been deposited");

        // What it could not, it recorded -- the owner's choice is kept, not discarded.
        var enqueued = icr.PeerKeyStore.PendingEnrollments.SingleOrDefault(p => p.CircleId == outOfReachCircle);
        Assert.That(enqueued, Is.Not.Null, "the out-of-reach circle should have been enqueued");
        Assert.That(enqueued!.RequestedByAppId, Is.EqualTo(app.AppId), "provenance: which app recorded it");
        Assert.That(enqueued.OwningAppId, Is.Not.Null.And.Not.EqualTo(app.AppId),
            "queued for the app that owns the circle, which is not the one that performed the review");

        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(outOfReachCircle), Is.False,
            "enqueuing must grant nothing");
        Assert.That(icr.ReviewedAt, Is.Not.Null, "and the review itself still counts");
    }

    [Test]
    public async Task EnqueuedEnrollment_IsReportedSeparatelyFromADeposit()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, reachableCircle, app) = await SetupAppWithReadCircleAsync(frodo);

        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "otherDrive", allowAnonymousReads: false);
        // Owned by some other app: a circle owned by no app can never be queued, because no app's
        // queue would claim it.
        var outOfReachCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(outOfReachCircle, "out-of-reach", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = otherDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: Guid.NewGuid());

        await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [reachableCircle, outOfReachCircle]);

        // The two states resolve differently -- one needs the Peer Key, the other needs an app that can
        // source the drive keys -- so a client has to be able to tell them apart.
        var info = await owner.GetConnectionInfoAsync(sam.Identity);
        Assert.That(info.Content!.AccessGrant.PendingCircleIds, Does.Contain(reachableCircle));
        Assert.That(info.Content.AccessGrant.PendingCircleIds, Does.Not.Contain(outOfReachCircle));
        var awaiting = info.Content.AccessGrant.AwaitingApps;
        Assert.That(awaiting.Select(a => a.CircleId), Does.Contain(outOfReachCircle));
        Assert.That(awaiting.Select(a => a.CircleId), Does.Not.Contain(reachableCircle));

        // Named, not just identified: a client has no app-id-to-name lookup, so without these it can
        // only say "waiting" where the owner wants to read "waiting on <app>".
        var entry = awaiting.Single(a => a.CircleId == outOfReachCircle);
        Assert.That(entry.CircleName, Is.EqualTo("out-of-reach"), "the circle should be named for the owner");
    }

    [Test]
    public async Task AppWithoutManageCircleMembership_CanReviewAndUnReview()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        // No permission keys at all. The review is the owner's judgment of a contact, reached only by
        // the owner or one of their apps, and the stamp grants nothing -- so gating it on a permission
        // about deciding circle membership was the wrong gate, and this pins that it is gone.
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "plainAppDrive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read, permissionKeys: Array.Empty<int>());
        var client = new V2ConnectionNetworkClient(app.Identity, app.Factory);

        var review = await client.MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
        Assert.That((await owner.GetConnectionInfoAsync(sam.Identity)).Content!.ReviewedAt, Is.Not.Null);

        var cleared = await client.ClearReviewAsync(sam.Identity);
        Assert.That(cleared.IsSuccessStatusCode, Is.True, $"clear failed: {cleared.StatusCode}");
        Assert.That((await owner.GetConnectionInfoAsync(sam.Identity)).Content!.ReviewedAt, Is.Null,
            "withdrawing the vouching is the same act in reverse and is gated the same way");
    }

    [Test]
    public async Task EnqueuingTheSameCircleTwice_LeavesOneEntry()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, _, app) = await SetupAppWithReadCircleAsync(frodo);
        var (outOfReachCircle, _) = await CreateOutOfReachCircleAsync(frodo, "out-of-reach");

        // A second review is an ordinary thing -- the owner opens the dialog again, or a client retries
        // -- and it must not leave the queue holding the same circle twice.
        var client = new V2ConnectionNetworkClient(app.Identity, app.Factory);
        var first = await client.MarkReviewedAsync(sam.Identity, [outOfReachCircle]);
        Assert.That(first.IsSuccessStatusCode, Is.True, $"first review failed: {first.StatusCode}");
        var second = await client.MarkReviewedAsync(sam.Identity, [outOfReachCircle]);
        Assert.That(second.IsSuccessStatusCode, Is.True, $"second review failed: {second.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Count(p => p.CircleId == outOfReachCircle), Is.EqualTo(1));
    }

    [Test]
    public async Task ACircleAlreadyInEffect_IsNotEnqueued()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, _, app) = await SetupAppWithReadCircleAsync(frodo);
        var (circle, _) = await CreateOutOfReachCircleAsync(frodo, "already-granted");

        // The owner grants it outright first; the app then reviews the same circle. Out of the app's
        // reach it may be, but there is nothing left to want -- queueing it would ask for work already
        // done, and would report the contact as awaiting an app it is not waiting for.
        var granted = await owner.GrantCircleAsync(circle, sam.Identity);
        Assert.That(granted.IsSuccessStatusCode, Is.True, $"owner grant failed: {granted.StatusCode}");

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == circle), Is.False);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.True, "and the real grant is untouched");
    }

    [Test]
    public async Task ACircleAlreadyDeposited_IsNotEnqueued()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        // The app that can source the drive's key deposits the circle...
        var (_, circle, depositor) = await SetupAppWithReadCircleAsync(frodo);
        var deposit = await new V2ConnectionNetworkClient(depositor.Identity, depositor.Factory)
            .GrantCircleAsync(circle, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        // ...and a different app, which cannot, reviews the same circle. A deposit is already further
        // along than a queued entry, so recording one behind it would only add a state to undo later.
        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "reviewerDrive", allowAnonymousReads: false);
        var reviewer = await AppSession.SetupAsync(frodo, otherDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var review = await new V2ConnectionNetworkClient(reviewer.Identity, reviewer.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == circle), Is.False);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle), Is.True,
            "and the deposit is still there, waiting on the Peer Key rather than on an app");
    }

    [Test]
    public async Task AnEntryForAnAppOwnedCircle_NamesTheAppThatCanCompleteIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, _, reviewer) = await SetupAppWithReadCircleAsync(frodo);

        // A circle belonging to another app entirely -- the case the queue is for. Ownership is copied
        // onto the entry so "has this app any work?" needs no scan of every circle definition.
        var mailDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(mailDrive, "mailDrive", allowAnonymousReads: false);
        var mail = await AppSession.SetupAsync(frodo, mailDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var mailCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(mailCircle, "mail-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = mailDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: mail.AppId);

        var review = await new V2ConnectionNetworkClient(reviewer.Identity, reviewer.Factory)
            .MarkReviewedAsync(sam.Identity, [mailCircle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        var icr = await GetIcrAsync(frodo, sam);
        var entry = icr.PeerKeyStore.PendingEnrollments.SingleOrDefault(p => p.CircleId == mailCircle);
        Assert.That(entry, Is.Not.Null, "the circle should have been enqueued");
        Assert.That(entry!.OwningAppId, Is.EqualTo(mail.AppId), "the app that owns the circle can complete it");
        Assert.That(entry.RequestedByAppId, Is.EqualTo(reviewer.AppId), "provenance: the app whose client asked");

        // And on the wire, named: the owner is meant to read "waiting on <app>", which a client cannot
        // produce from an id because it has no app-id-to-name lookup of its own.
        var info = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetConnectionInfoAsync(sam.Identity);
        var awaiting = info.Content!.AccessGrant.AwaitingApps.Single(a => a.CircleId == mailCircle);

        Assert.That(awaiting.CircleName, Is.EqualTo("mail-circle"));
        Assert.That(awaiting.AppId, Is.EqualTo(mail.AppId));
        Assert.That(awaiting.AppName, Is.Not.Null.And.Not.Empty, "the owning app must be named, not just identified");
    }

    [Test]
    public async Task AnEnqueuedCircle_ConfersNothingUntilItIsCompleted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        await owner.ClearReviewAsync(sam.Identity);

        var (_, _, app) = await SetupAppWithReadCircleAsync(frodo);
        var (circle, _) = await CreateOutOfReachCircleAsync(frodo, "out-of-reach");

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [circle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        // An entry is a record of what the owner asked for, not a grant of it: nothing here may behave
        // as though sam were in the circle, or she would hold access nobody was able to key.
        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == circle), Is.True,
            "precondition: the circle should be enqueued");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.False);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle), Is.False);

        Assert.That((await owner.GetCircleMembersAsync(circle)).Content!.Any(m => m == sam.Identity), Is.False,
            "an enqueued circle must not produce a membership row");
        Assert.That((await owner.GetPendingCircleMembersAsync(circle)).Content!.Any(m => m.OdinId == sam.Identity),
            Is.False, "nor count as a deposited-but-pending member, which is a different state entirely");
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession frodo, OwnerSession target)
    {
        var icr = await Host.GetTenantScope(frodo.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(target.Identity);
        Assert.That(icr, Is.Not.Null);
        return icr!;
    }

    /// <summary>
    /// A read-bearing circle on a drive of its own, which the app from
    /// <see cref="SetupAppWithReadCircleAsync"/> has nothing on -- the shape of another app's circle
    /// ticked from this one's review dialog.
    /// </summary>
    /// <param name="owningAppId">
    /// The app this circle belongs to.  It has to belong to one: a circle owned by no app can never be
    /// queued, because no app's queue would claim it.  Defaults to an id standing in for some other
    /// installed app -- the reviewing app is not it, which is the point.
    /// </param>
    private static async Task<(Guid circle, TargetDrive drive)> CreateOutOfReachCircleAsync(
        OwnerSession frodo, string name, Guid? owningAppId = null)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, $"{name}Drive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, name, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: owningAppId ?? Guid.NewGuid());

        return (circle, drive);
    }

    private static async Task<(TargetDrive drive, Guid circle, AppSession app)> SetupAppWithReadCircleAsync(
        OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "readDrive", allowAnonymousReads: false);

        // App-owned: an app cannot enrol anyone into a circle that belongs to no app.
        var appId = Guid.NewGuid();
        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "read-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership }, knownAppId: appId);

        return (drive, circle, app);
    }
}
