#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
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
/// Covers the pending-member surface: circles/pending, and the pendingMembers sibling on
/// circles/with-members.
/// </summary>
/// <remarks>
/// A deposited grant leaves an identity in a state the circle APIs previously could not describe -- asked
/// for, not yet in effect, and therefore absent from the member list with no explanation.  These report
/// that beside the members rather than among them, so a client that does not know the field keeps
/// behaving exactly as it did.
/// </remarks>
[TestFixture]
public class PendingCircleMemberTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task DepositedGrant_IsReportedAsPending_WithWhoAskedAndWhen()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var before = UnixTimeUtcNow();
        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);

        var members = await owner.GetCircleMembersAsync(circle);
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.False,
            "a pending identity is not a member and must not be listed as one");

        var pending = await owner.GetPendingCircleMembersAsync(circle);
        Assert.That(pending.IsSuccessStatusCode, Is.True, $"pending lookup failed: {pending.StatusCode}");
        Assert.That(pending.Content!.Count, Is.EqualTo(1));

        var entry = pending.Content[0];
        Assert.That(entry.OdinId, Is.EqualTo(sam.Identity));
        Assert.That(entry.DepositingAppId, Is.EqualTo(app.AppId),
            "the UI needs to say which app asked");
        Assert.That(entry.Deposited.milliseconds, Is.GreaterThanOrEqualTo(before),
            "the UI needs to say how long it has been waiting");
    }

    [Test]
    public async Task CirclesWithMembers_ReportsPendingBeside_WithoutChangingMembers()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);
        await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);

        var response = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetCirclesWithMembersAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var entry = response.Content!.SingleOrDefault(c => c.Circle.Id == circle);
        Assert.That(entry, Is.Not.Null, "the circle should be listed");

        // The compatibility promise: Members is untouched, so a client that ignores the new field is
        // unaffected by any of this.
        Assert.That(entry!.Members.Any(m => m == sam.Identity), Is.False,
            "a pending identity must not appear in Members");
        Assert.That(entry.PendingMembers.Any(p => p.OdinId == sam.Identity), Is.True,
            "and must appear in PendingMembers");
    }

    [Test]
    public async Task OnceConverted_TheIdentityMovesFromPendingIntoMembers()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);
        await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);

        var owner = new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory);
        Assert.That((await owner.GetPendingCircleMembersAsync(circle)).Content!.Count, Is.EqualTo(1),
            "precondition: pending before conversion");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        await scope.Resolve<CircleNetworkService>().ConvertDepositedGrantsForConnectedIdentitiesAsync(
            await BuildOwnerContextAsync(scope, frodo), CancellationToken.None);

        Assert.That((await owner.GetPendingCircleMembersAsync(circle)).Content!, Is.Empty,
            "nothing should still be pending once converted");
        Assert.That((await owner.GetCircleMembersAsync(circle)).Content!.Any(m => m == sam.Identity), Is.True,
            "and the identity should now be a real member");
    }

    [Test]
    public async Task AppWithoutReadCircleMembership_CannotListPendingMembers()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "drive", allowAnonymousReads: false);

        // App-owned: an app cannot enrol anyone into a circle that belongs to no app.
        var appId = Guid.NewGuid();
        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);

        // Who is pending for a circle is circle-membership information, gated the same way the member
        // list is.
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read,
            permissionKeys: Array.Empty<int>(), knownAppId: appId);

        var response = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GetPendingCircleMembersAsync(circle);
        Assert.That(response.IsSuccessStatusCode, Is.False, "listing pending members needs ReadCircleMembership");
    }

    private static long UnixTimeUtcNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
