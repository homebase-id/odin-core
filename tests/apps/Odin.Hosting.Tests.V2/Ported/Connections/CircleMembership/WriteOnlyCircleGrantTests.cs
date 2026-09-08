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
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers the direct-mint branch of <c>CircleNetworkService.EnrollInCircleInternalAsync</c>: an app with
/// no master key adds a peer to a circle whose grants carry no key material, and the membership is live
/// straight away rather than pending.
/// </summary>
/// <remarks>
/// A write/react grant is a plaintext <c>{driveId, permission}</c> record -- <c>CreateDriveGrant</c> only
/// reaches for a storage key when the permission has Read -- so the connection's Peer Key, which an app
/// cannot obtain, is not needed to mint it.  Depositing such a grant would seal nothing and leave the
/// member out of the circle until something happened to touch the connection.  Read-bearing circles are
/// unaffected and still deposit; that is <see cref="DepositGrantTests"/>.
/// </remarks>
[TestFixture]
public class WriteOnlyCircleGrantTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppGrantsWriteOnlyCircle_MintsDirectly_MembershipIsLiveImmediately()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithWriteOnlyCircleAsync(frodo);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"grant should succeed, got {response.StatusCode}");

        // The point of the change: a real member, now -- not a pending deposit.
        var members = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetCircleMembersAsync(circle);
        Assert.That(members.IsSuccessStatusCode, Is.True);
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.True,
            "sam should be a circle member immediately; a write-only grant needs no Peer Key");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.True, "a real CircleGrant should exist");
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle), Is.False,
            "nothing should be left pending");

        // The minted grant carries the permission and no key material, which is what let it be minted.
        var driveGrants = icr.PeerKeyStore.CircleGrants[circle].KeyStoreKeyEncryptedDriveGrants;
        Assert.That(driveGrants.Count, Is.EqualTo(1));
        Assert.That(driveGrants[0].PermissionedDrive.Permission,
            Is.EqualTo(DrivePermission.Write | DrivePermission.React));
        Assert.That(driveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Null,
            "a write-only grant must carry no storage key");
    }

    [Test]
    public async Task AppCannotGrantWriteOnADriveItCannotWrite()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "appDrive", allowAnonymousReads: false);

        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "otherDrive", allowAnonymousReads: false);

        // Write-only circle, but on a drive the app has nothing on.  Minting needs no key here, so
        // nothing cryptographic stops it -- only the scope check does, which is why it exists.
        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "out-of-scope-write", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = otherDrive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Write,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(response.IsSuccessStatusCode, Is.False,
            "an app must not grant a drive permission it does not hold itself");

        var icr = await GetIcrAsync(frodo, sam);
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.False, "nothing should have been minted");
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle), Is.False,
            "nothing should have been deposited either");
    }

    [Test]
    public async Task AppWithoutManageCircleMembership_CannotGrantWriteOnlyCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "drive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "write-only", new PermissionSetGrantRequest
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

        // Drive access alone is not enough; needing no Peer Key does not mean needing no permission.
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Write, permissionKeys: Array.Empty<int>());

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"expected 403 without ManageCircleMembership, got {response.StatusCode}");
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession frodo, OwnerSession sam)
    {
        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr, Is.Not.Null);
        return icr!;
    }

    private static async Task<(TargetDrive drive, Guid circle, AppSession app)> SetupAppWithWriteOnlyCircleAsync(
        OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "chat-shaped", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "chat-shaped-circle", new PermissionSetGrantRequest
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

        return (drive, circle, app);
    }
}
