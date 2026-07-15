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
/// Covers the owner (master-key) branch of <c>CircleNetworkService.GrantCircleAsync</c>: a direct
/// grant is minted immediately as a real <c>CircleGrant</c> (regression check for the refactor that
/// extracted <c>FanOutAppCircleGrantsAsync</c>), and — as a side effect — the owner's touch also
/// converts any pending app-deposited grants on that same connection.
/// </summary>
[TestFixture]
public class OwnerGrantConversionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task OwnerGrant_ForNewCircle_IsImmediatelyReal()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var circleZ = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleZ, "circleZ", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            // A circle must grant at least one drive or one permission — this one carries no drives,
            // so give it a harmless circle-valid permission key instead.
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });

        var client = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        var grant = await client.GrantCircleAsync(circleZ, sam.Identity);
        Assert.That(grant.IsSuccessStatusCode, Is.True, $"owner grant failed: {grant.StatusCode}");

        var members = await client.GetCircleMembersAsync(circleZ);
        Assert.That(members.IsSuccessStatusCode, Is.True);
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.True,
            "sam should be an immediate, real circle member after an owner grant");
    }

    [Test]
    public async Task OwnerGrant_OnDifferentCircle_AlsoConvertsPendingDeposit()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // An app deposits circleA (pending) first.
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

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleA, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var beforeOwnerTouch = await storage.GetAsync(sam.Identity);
        Assert.That(beforeOwnerTouch!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.True,
            "precondition: circleA deposit should still be pending");

        // Now the owner grants a DIFFERENT circle directly.
        var circleB = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleB, "circleB", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });

        var ownerGrant = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GrantCircleAsync(circleB, sam.Identity);
        Assert.That(ownerGrant.IsSuccessStatusCode, Is.True, $"owner grant failed: {ownerGrant.StatusCode}");

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.CircleGrants.ContainsKey(circleB), Is.True, "circleB should be a real grant");
        Assert.That(after.PeerKeyStore.CircleGrants.ContainsKey(circleA), Is.True,
            "circleA's pending deposit should have been converted as a side effect of the owner's grant touch");
        Assert.That(after.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.False,
            "circleA should no longer be a pending deposit");
    }
}
