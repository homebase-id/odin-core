#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// What a peer's call can and cannot finish.
/// </summary>
/// <remarks>
/// A peer arriving brings the Peer Key, which is what a deposited grant is waiting for -- so those
/// convert.  It brings no app key, and a pending enrollment waits on an app that can source the drive
/// keys, so those must be left exactly as they are.  The two states exist because their resolutions
/// differ, and this is the test that holds them apart.
/// <para>
/// Kept out of <see cref="PeerCatConversionTests"/> so that file stays as it is on main: this exercises
/// the enrolment path, which is new, while that one covers the deposit conversion that predates it.
/// </para>
/// </remarks>
[TestFixture]
public class PeerCallPendingEnrollmentTests : V2Fixture
{
    private const int MessageFileType = 7788;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task PeerCall_ConvertsTheDeposit_ButLeavesAPendingEnrollmentAlone()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sam can write to Frodo's trigger drive, which is what lets her server call in and set off
        // Frodo's conversion of whatever he holds pending about her.
        var trigger = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "trigger");

        // App-owned: an app cannot enrol anyone into a circle that belongs to no app.
        var appId = Guid.NewGuid();
        var circleY = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleY, "circleY", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = trigger, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);

        var app = await AppSession.SetupAsync(frodo, trigger, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership }, knownAppId: appId);

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleY, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        // ...and a circle on a drive the app has nothing on, which its review can only record.
        var otherDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(otherDrive, "otherDrive", allowAnonymousReads: false);
        var awaitingCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(awaitingCircle, "awaiting-app", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = otherDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: Guid.NewGuid());

        var review = await new V2ConnectionNetworkClient(app.Identity, app.Factory)
            .MarkReviewedAsync(sam.Identity, [awaitingCircle]);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        var metadata = SampleMetadataData.Create(fileType: MessageFileType, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await sam.Drives.Writer.UploadNewMetadata(trigger.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { frodo.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");

        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(trigger);

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);

        Assert.That(icr!.PeerKeyStore.CircleGrants.ContainsKey(circleY), Is.True,
            "the deposit had its key material already and only wanted the Peer Key, which the peer call supplies");

        // The other kind of pending is not the peer's to resolve: a peer brings the Peer Key, not the
        // app key, and this entry is waiting on an app that can read otherDrive. Converting it here
        // would either fail or mint a grant with no usable key.
        Assert.That(icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == awaitingCircle), Is.True,
            "a pending enrollment must survive the peer call untouched");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(awaitingCircle), Is.False);
        Assert.That(icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == awaitingCircle), Is.False);
    }
}
