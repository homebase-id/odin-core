#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers <c>CircleNetworkService.CreateDepositedGrantAsync</c> / the app branch of
/// <c>GrantCircleAsync</c>: an app holding <see cref="PermissionKeys.ManageCircleMembership"/> (but no
/// master key) can add an already-connected peer to a circle by depositing a sealed grant to the
/// connection's <c>PeerKeyStore.WriteOnlyKeyPair</c>. Drive storage keys for the deposit are sourced
/// from the app's own permission context (<c>PermissionContextStorageKeySource</c>), so the app can
/// only deposit what it itself can read/write; anything else fails the whole call (all-or-nothing).
/// </summary>
[TestFixture]
public class DepositGrantTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppCannotDeposit_IntoACircleItDoesNotOwn()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var driveA = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(driveA, "driveA", allowAnonymousReads: false);

        var circleA = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleA, "circleA", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveA, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        // App has matching drive access, but circleA is the owner's, not the app's. Owning a circle is
        // what lets an app add to it; no permission key stands in for that.
        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read, permissionKeys: Array.Empty<int>());

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"expected 403 for a circle the app does not own, got {response.StatusCode}");
    }

    [Test]
    public async Task AppWithPermissionAndMatchingDriveScope_DepositSucceeds_ButGrantIsPending()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (driveA, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"deposit should succeed, got {response.StatusCode}");

        // Not yet a real circle member — the deposit is still pending conversion.
        var members = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetCircleMembersAsync(circleA);
        Assert.That(members.IsSuccessStatusCode, Is.True);
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.False,
            "sam should not yet appear as a circle member — the deposit hasn't converted");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr, Is.Not.Null);
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.True,
            "a DepositedGrant for circleA should exist");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circleA), Is.False,
            "no real CircleGrant should exist yet");
    }

    [Test]
    public async Task AppDepositsCircleWithOutOfScopeDrive_FailsEntirely_NoPartialState()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var driveA = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(driveA, "driveA", allowAnonymousReads: false);

        var driveOutOfScope = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(driveOutOfScope, "driveOOS", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read, permissionKeys: Array.Empty<int>());

        // Circle spans two drives and belongs to the app, which is only granted one of them.
        var circleMulti = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleMulti, "circleMulti", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveA, Permission = DrivePermission.Read } },
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveOutOfScope, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: app.AppId);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleMulti, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.False, "deposit spanning an out-of-scope drive must fail entirely");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.CircleGrants.ContainsKey(circleMulti), Is.False,
            "no CircleGrant should have been created");
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleMulti), Is.False,
            "no partial DepositedGrant should have been saved — the deposit is all-or-nothing");
    }

    [Test]
    public async Task DuplicateDeposit_SecondCallIsANoOp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);
        var client = new V2ConnectionNetworkClient(app.Identity, app.Factory);

        var first = await client.GrantCircleAsync(circleA, sam.Identity);
        Assert.That(first.IsSuccessStatusCode, Is.True, $"first deposit should succeed, got {first.StatusCode}");

        // Enrolment is idempotent: someone already deposited is left as they are.
        var second = await client.GrantCircleAsync(circleA, sam.Identity);
        Assert.That(second.IsSuccessStatusCode, Is.True, $"a repeated add should be a no-op, got {second.StatusCode}");

        var icr = await Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>().GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Count(d => d.CircleId == circleA), Is.EqualTo(1));
    }

    [Test]
    public async Task MissingWriteOnlyKeyPair_DepositFails_WithProvisioningError()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // Simulate a pre-migration connection: null out the write-only keypair that the accept flow
        // would normally have provisioned.
        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var icrBefore = await storage.GetAsync(sam.Identity);
        Assert.That(icrBefore!.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null,
            "precondition: normal accept flow should have provisioned the write-only keypair");
        icrBefore.PeerKeyStore.WriteOnlyKeyPair = null;
        await storage.UpsertAsync(icrBefore, ctx);

        var (_, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            $"deposit against a connection with no write-only keypair should 400, got {response.StatusCode}");
    }

    [Test]
    public async Task AppCannotAddAnUnreviewedContact_ToAManualCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // An auto-connection is made without the owner reviewing anyone.
        var header = new ConnectionRequestHeader
        {
            Recipient = sam.Identity,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>()
        };
        var autoConnect = await frodo.Connections.AutoConnectAsync(header);
        Assert.That(autoConnect.IsSuccessStatusCode, Is.True, $"auto-connect failed: {autoConnect.StatusCode}");
        Assert.That(autoConnect.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected));

        var (_, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);

        // circleA is managed by hand (granted on neither connect nor review), and membership of such a
        // circle implies review: an unreviewed contact is refused, with a reason the caller can show.
        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            $"an unreviewed contact should be refused, got {response.StatusCode}");
        Assert.That(TestUtils.ParseProblemDetails(response.Error!), Is.EqualTo(OdinClientErrorCode.ContactNotReviewed));

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.False,
            "nothing should have been deposited for the refused contact");
    }

    [Test]
    public async Task PendingDeposit_IsVisibleAsPendingCircleId_ThenMovesToCircleGrants_OnConversion()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        var infoBefore = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(infoBefore.IsSuccessStatusCode, Is.True);
        Assert.That(infoBefore.Content!.AccessGrant.PendingCircleIds, Does.Contain(circleA),
            "circleA should be visible as pending before conversion");
        Assert.That(infoBefore.Content.AccessGrant.CircleGrants.Any(cg => cg.CircleId == circleA), Is.False,
            "circleA must not appear as a real grant yet");

        // Owner touches a different circle on the same connection — the known trigger that also
        // converts pending deposits (OwnerGrantConversionTests).
        var circleB = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleB, "circleB", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });
        var ownerGrant = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GrantCircleAsync(circleB, sam.Identity);
        Assert.That(ownerGrant.IsSuccessStatusCode, Is.True, $"owner grant failed: {ownerGrant.StatusCode}");

        var infoAfter = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(infoAfter.IsSuccessStatusCode, Is.True);
        Assert.That(infoAfter.Content!.AccessGrant.PendingCircleIds, Does.Not.Contain(circleA),
            "circleA should no longer be reported pending once converted");
        Assert.That(infoAfter.Content.AccessGrant.CircleGrants.Any(cg => cg.CircleId == circleA), Is.True,
            "circleA should now appear as a real grant");
    }

    /// <summary>
    /// Creates driveA on Frodo, an app on Frodo holding exactly Read on driveA, and a circle that app
    /// owns granting Read on driveA — the minimal setup for a deposit that stays fully in the app's
    /// own drive scope. No permission key: owning the circle is what lets the app add to it.
    /// </summary>
    private static async Task<(TargetDrive driveA, Guid circleA, AppSession app)> SetupAppWithMatchingCircleAsync(OwnerSession frodo)
    {
        var driveA = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(driveA, "driveA", allowAnonymousReads: false);

        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read, permissionKeys: Array.Empty<int>());

        var circleA = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleA, "circleA", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveA, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: app.AppId);

        return (driveA, circleA, app);
    }

}
