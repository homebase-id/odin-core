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
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

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
    public async Task AppWithoutManageCircleMembership_CannotDepositGrant()
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

        // App has matching drive access but NOT the ManageCircleMembership permission key.
        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read, permissionKeys: Array.Empty<int>());

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"expected 403 without ManageCircleMembership, got {response.StatusCode}");
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

        // Circle spans two drives; the app is only granted one of them.
        var circleMulti = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleMulti, "circleMulti", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveA, Permission = DrivePermission.Read } },
                new() { PermissionedDrive = new PermissionedDrive { Drive = driveOutOfScope, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

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
    public async Task DuplicateDeposit_SecondCallFails_AlreadyMember()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circleA, app) = await SetupAppWithMatchingCircleAsync(frodo);
        var client = new V2ConnectionNetworkClient(app.Identity, app.Factory);

        var first = await client.GrantCircleAsync(circleA, sam.Identity);
        Assert.That(first.IsSuccessStatusCode, Is.True, $"first deposit should succeed, got {first.StatusCode}");

        var second = await client.GrantCircleAsync(circleA, sam.Identity);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            $"duplicate deposit should fail with 400 (already member), got {second.StatusCode}");
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

    /// <summary>
    /// Creates driveA on Frodo, a circle granting Read on driveA, and an app on Frodo holding
    /// exactly Read on driveA plus <see cref="PermissionKeys.ManageCircleMembership"/> — the minimal
    /// setup for a deposit that stays fully in the app's own drive scope.
    /// </summary>
    private static async Task<(TargetDrive driveA, Guid circleA, AppSession app)> SetupAppWithMatchingCircleAsync(OwnerSession frodo)
    {
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

        var app = await AppSession.SetupAsync(frodo, driveA, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        return (driveA, circleA, app);
    }

    /// <summary>
    /// Builds an owner context carrying the master key by replaying the production path used by
    /// <c>VersionUpgradeService</c> (<see cref="Odin.Services.Authentication.Owner.OwnerAuthenticationService.UpdateOdinContextAsync"/>).
    /// </summary>
    private async Task<Odin.Services.Base.IOdinContext> BuildOwnerContextAsync(Autofac.ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<Odin.Services.Authentication.Owner.OwnerAuthenticationService>();
        var odinContext = new Odin.Services.Base.OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };
        var clientContext = new Odin.Services.Base.OdinClientContext
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
