#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers <c>CircleNetworkService.ProcessPendingEnrollmentsForAppAsync</c> -- the app (or the owner)
/// coming back to finish circle enrollments some other client recorded because it could not source the
/// drives' storage keys itself.
/// </summary>
/// <remarks>
/// The whole point of the queue is that the owner's choice survives a client that could not carry it
/// out, so what matters here is who is allowed to carry out what: an app finishes its own circles and
/// nobody else's, the owner console finishes everything, and an entry that still cannot be completed
/// stays put rather than being dropped or half-applied.
/// <para>
/// Note that an app caller is the owner acting -- <c>IsOwner</c> is true and <c>HasMasterKey</c> is
/// false for both an app and the console -- so these tests are the only thing pinning that the app id,
/// not the caller's security level, is what scopes the work.
/// </para>
/// </remarks>
[TestFixture]
public class PendingEnrollmentProcessingTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task AnApp_CompletesOnlyItsOwnCircles_AndLeavesAnotherAppsAlone()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        var photos = await SetupAppOwningAReadCircleAsync(frodo, "photos");

        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle, photos.Circle);

        var result = await new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True, $"process failed: {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(1),
            "the mail app should have completed its own circle and only its own");
        Assert.That(result.Content.ConnectionsProcessed, Is.EqualTo(1));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == mail.Circle), Is.True,
            "mail's circle should have moved on to a deposit");

        // Photos' entry is untouched: the mail app has no business acting on a circle whose drives it
        // cannot read, and the queue is what keeps that choice alive until photos runs.
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Select(p => p.CircleId.Value),
            Is.EquivalentTo(new[] { photos.Circle }));
    }

    [Test]
    public async Task OwnerConsole_IsScopedToNoApp_AndCompletesEveryPendingEnrollment()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        var photos = await SetupAppOwningAReadCircleAsync(frodo, "photos");

        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle, photos.Circle);

        // The console carries no app id, which is what makes it unscoped -- it is not that the owner
        // has more permission here, it is that it is not any app in particular.
        var result = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True, $"process failed: {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(2), "both apps' entries should be finished");
        Assert.That(result.Content.ConnectionsProcessed, Is.EqualTo(1));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments, Is.Empty);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(mail.Circle), Is.True);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(photos.Circle), Is.True);
    }

    [Test]
    public async Task AppWithoutManageCircleMembership_CanStillCompleteItsOwnQueue()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // ReadCircleMembership only: the app is carrying out a decision the owner already made, so
        // the permission about *deciding* membership has nothing to say about it. Reading the circle
        // definition it is about to grant is a different gate, and that one still applies.
        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail",
            permissionKeys: new[] { PermissionKeys.ReadCircleMembership });

        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle);

        var result = await new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True,
            $"an app without ManageCircleMembership must still be able to finish its own queue, got {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(1));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments, Is.Empty);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == mail.Circle), Is.True);
    }

    [Test]
    public async Task AppThatOwnsTheCircleButCannotSourceItsKeys_LeavesTheEntryQueued()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");

        // A second circle owned by mail, but over a drive mail was never granted. Owning the circle is
        // what selects the entry; it is not what authorizes the grant, so scope is re-checked here and
        // this one must fail that check rather than ride in on ownership.
        var strangerDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(strangerDrive, "strangerDrive", allowAnonymousReads: false);
        var unreachableCircle = Guid.NewGuid();
        await CreateReadCircleAsync(frodo, unreachableCircle, "mail-but-unreachable", strangerDrive, mail.App.AppId);

        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle, unreachableCircle);

        var result = await new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True, $"process failed: {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(1), "only the circle it can actually grant");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Select(p => p.CircleId.Value),
            Is.EquivalentTo(new[] { unreachableCircle }),
            "the entry it could not complete waits for the owner rather than being dropped");
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == unreachableCircle), Is.False,
            "and nothing partial should have been written for it");
    }

    [Test]
    public async Task EntryForACircleDeletedWhileItWaited_IsDropped_NotRetriedForever()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle);

        // An enqueued circle has no members, so the owner can still delete it -- and then there is
        // nothing left to grant. Same reasoning as a deposit for a deleted circle (PeerCatConversionTests).
        var deleted = await new UniversalCircleNetworkApiClient(frodo.Identity, frodo.Factory)
            .DeleteCircleDefinition(mail.Circle);
        Assert.That(deleted.IsSuccessStatusCode, Is.True, $"circle delete failed: {deleted.StatusCode}");

        var result = await new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True, $"process failed: {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(0), "nothing was granted");
        Assert.That(result.Content.ConnectionsProcessed, Is.EqualTo(1), "but the connection was tidied");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments, Is.Empty,
            "a dangling entry must not be kept and retried on every pass");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(mail.Circle), Is.False);
    }

    [Test]
    public async Task AFailingEntry_StaysQueued_AndDoesNotStrandTheOthersOnThatConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // One app, one drive, two of its circles: a read circle (which must be deposited, so it needs
        // the connection's write-only key) and a write/react circle (minted outright, which does not).
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "mailDrive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(frodo, drive,
            DrivePermission.Read | DrivePermission.Write | DrivePermission.React,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var readCircle = Guid.NewGuid();
        await CreateReadCircleAsync(frodo, readCircle, "mail-read", drive, app.AppId);

        var writeCircle = Guid.NewGuid();
        await CreateCircleAsync(frodo, writeCircle, "mail-write", drive,
            DrivePermission.Write | DrivePermission.React, app.AppId);

        await EnqueueViaReviewAsync(frodo, sam.Identity, readCircle, writeCircle);

        // Break exactly one of the two: with no write-only key pair on the store there is nothing to
        // seal a deposit to, so the read circle throws while the write circle is unaffected.
        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ownerContext = await BuildOwnerContextAsync(scope, frodo);
        var broken = await storage.GetAsync(sam.Identity);
        broken!.PeerKeyStore.WriteOnlyKeyPair = null;
        await storage.UpsertAsync(broken, ownerContext);

        var result = await new V2ConnectionNetworkClient(app.Identity, app.Factory).ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True,
            $"one bad entry must not fail the whole call, got {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(1),
            "the entry that could be completed should have been");

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(writeCircle), Is.True);
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Select(p => p.CircleId.Value),
            Is.EquivalentTo(new[] { readCircle }),
            "the failed entry stays queued so the next pass tries it again");
    }

    [Test]
    public async Task TwoEntriesOnOneConnection_AreBothCompleted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");

        var second = Guid.NewGuid();
        await CreateReadCircleAsync(frodo, second, "mail-second", mail.Drive, mail.App.AppId);

        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle, second);

        var result = await new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(result.IsSuccessStatusCode, Is.True, $"process failed: {result.StatusCode}");
        Assert.That(result.Content!.EnrollmentsCompleted, Is.EqualTo(2));
        Assert.That(result.Content.ConnectionsProcessed, Is.EqualTo(1), "both entries are on the one connection");

        // Enrollment saves the connection itself, so the copy the loop is holding goes stale after the
        // first entry; a pass that wrote that copy back would take the second deposit down with it.
        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Select(d => d.CircleId.Value),
            Is.SupersetOf(new[] { mail.Circle, second }), "both deposits must survive the strip");
        Assert.That(icr.PeerKeyStore.PendingEnrollments, Is.Empty);
    }

    [Test]
    public async Task ProcessEndpoint_ReportsWhatItActuallyDid_AcrossConnections()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline-sam");
        await PeerFlow.CreatePeerDriveAsync(frodo, merry, DrivePermission.Read, "baseline-merry");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle);
        await EnqueueViaReviewAsync(frodo, merry.Identity, mail.Circle);

        var client = new V2ConnectionNetworkClient(mail.App.Identity, mail.App.Factory);

        // The counts are the only thing a client has to go on -- there is no per-entry response -- so
        // they have to be the truth rather than an attempt count.
        var first = await client.ProcessPendingEnrollmentsAsync();
        Assert.That(first.IsSuccessStatusCode, Is.True, $"process failed: {first.StatusCode}");
        Assert.That(first.Content!.ConnectionsProcessed, Is.EqualTo(2));
        Assert.That(first.Content.EnrollmentsCompleted, Is.EqualTo(2));

        var second = await client.ProcessPendingEnrollmentsAsync();
        Assert.That(second.IsSuccessStatusCode, Is.True);
        Assert.That(second.Content!.ConnectionsProcessed, Is.EqualTo(0), "nothing is left to do");
        Assert.That(second.Content.EnrollmentsCompleted, Is.EqualTo(0));
    }

    [Test]
    public async Task Guest_CannotProcessEnrollments()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "guestDrive", allowAnonymousReads: false);
        var guest = await GuestSession.SetupAsync(frodo, drive, DrivePermission.Read);

        // Completing enrollments is the owner's side of the connection acting on the owner's own
        // choices; a visiting identity has no part in it and is turned away at the route.
        var response = await new V2ConnectionNetworkClient(guest.Identity, guest.Factory)
            .ProcessPendingEnrollmentsAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"expected a guest to be refused, got {response.StatusCode}");
    }

    [Test]
    public async Task OwnerPrePass_CompletesAnEntryOutright_RatherThanLeavingADeposit()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle);

        // Replays the upgrade's pending-enrollment-pre-pass: the same call the phase makes, with the
        // same kind of owner context. The phase ordering itself (before the deposit drain) is not
        // under test here.
        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, frodo);
        var (connectionsProcessed, enrollmentsCompleted) = await scope.Resolve<CircleNetworkService>()
            .ProcessPendingEnrollmentsForAppAsync(ctx);

        Assert.That(connectionsProcessed, Is.EqualTo(1));
        Assert.That(enrollmentsCompleted, Is.EqualTo(1));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.PendingEnrollments, Is.Empty);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == mail.Circle), Is.False,
            "the owner sources every storage key and reaches the Peer Key, so there is no deposit stage to wait in");

        // A grant recorded without its key would leave sam in the circle and able to decrypt nothing.
        var driveGrants = icr.PeerKeyStore.CircleGrants[mail.Circle].KeyStoreKeyEncryptedDriveGrants;
        Assert.That(driveGrants.Count, Is.EqualTo(1));
        Assert.That(driveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Not.Null,
            "the owner's completion must carry a real storage key");
    }

    [Test]
    public async Task PrePass_WithNothingPending_IsANoOp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // The pass runs on every upgrade, and most identities will never have an entry.
        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var (connectionsProcessed, enrollmentsCompleted) = await scope.Resolve<CircleNetworkService>()
            .ProcessPendingEnrollmentsForAppAsync(await BuildOwnerContextAsync(scope, frodo));

        Assert.That(connectionsProcessed, Is.EqualTo(0));
        Assert.That(enrollmentsCompleted, Is.EqualTo(0));
    }

    [Test]
    public async Task PrePass_RunTwice_CompletesOnceAndThenDoesNothing()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var mail = await SetupAppOwningAReadCircleAsync(frodo, "mail");
        await EnqueueViaReviewAsync(frodo, sam.Identity, mail.Circle);

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var service = scope.Resolve<CircleNetworkService>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var first = await service.ProcessPendingEnrollmentsForAppAsync(ctx);
        Assert.That(first.enrollmentsCompleted, Is.EqualTo(1));

        // Safe to re-run: an upgrade repeats this pass, and a completed entry is gone rather than
        // re-granted.
        var second = await service.ProcessPendingEnrollmentsForAppAsync(ctx);
        Assert.That(second.connectionsProcessed, Is.EqualTo(0));
        Assert.That(second.enrollmentsCompleted, Is.EqualTo(0));

        var icr = await GetIcrAsync(frodo, sam.Identity);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(mail.Circle), Is.True,
            "the grant made by the first run should still be there");
    }

    // -------------------------------------------------------------------------------------------

    private sealed record AppOwnedCircle(AppSession App, TargetDrive Drive, Guid Circle);

    /// <summary>
    /// An app, a drive only it can read, and a read-bearing circle owned by that app -- the shape the
    /// queue exists for, since no other client can source that drive's storage key.
    /// </summary>
    private static async Task<AppOwnedCircle> SetupAppOwningAReadCircleAsync(
        OwnerSession owner, string label, IReadOnlyList<int>? permissionKeys = null)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, $"{label}Drive", allowAnonymousReads: false);

        // The app comes first: ownership is set when the circle is created and cannot be reassigned.
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.Read,
            permissionKeys: permissionKeys ?? new[] { PermissionKeys.ManageCircleMembership });

        var circle = Guid.NewGuid();
        await CreateReadCircleAsync(owner, circle, $"{label}-circle", drive, app.AppId);

        return new AppOwnedCircle(app, drive, circle);
    }

    private static Task CreateReadCircleAsync(OwnerSession owner, Guid circleId, string name, TargetDrive drive,
        Guid? appId) =>
        CreateCircleAsync(owner, circleId, name, drive, DrivePermission.Read, appId);

    private static async Task CreateCircleAsync(OwnerSession owner, Guid circleId, string name, TargetDrive drive,
        DrivePermission permission, Guid? appId)
    {
        await owner.Admin.CreateCircle(circleId, name, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);
    }

    /// <summary>
    /// Produces the pending entries by the only route that makes them: a review run from a client that
    /// cannot grant the circles the owner ticked.
    /// </summary>
    /// <remarks>
    /// The reviewing app is given a drive of its own and nothing else, so every circle named here is
    /// out of its reach -- which is what a review dialog listing another app's circles looks like.
    /// </remarks>
    private static async Task EnqueueViaReviewAsync(OwnerSession owner, OdinId target, params Guid[] circleIds)
    {
        var reviewerDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(reviewerDrive, $"reviewerDrive-{Guid.NewGuid():N}", allowAnonymousReads: false);
        var reviewer = await AppSession.SetupAsync(owner, reviewerDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var review = await new V2ConnectionNetworkClient(reviewer.Identity, reviewer.Factory)
            .MarkReviewedAsync(target, circleIds.Select(c => (Odin.Core.GuidId)c).ToList());
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr, Is.Not.Null, $"no connection record for {target}");
        return icr!;
    }

    /// <summary>
    /// An owner context carrying the master key, built the way <c>VersionUpgradeService</c> builds one,
    /// so a phase can be replayed by calling the service directly.
    /// </summary>
    private async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }
}
