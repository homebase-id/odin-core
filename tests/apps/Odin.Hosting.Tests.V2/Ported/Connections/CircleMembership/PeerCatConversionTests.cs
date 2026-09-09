#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers the peer-CAT triggered conversion path: <c>CircleNetworkService.CreateTransitPermissionContextAsync</c>
/// opportunistically converts pending deposits (<c>TryConvertDepositedGrantsAtPeerAuthAsync</c>) and fans out
/// <c>AppCircleGrant</c>s (<c>FanOutAppCircleGrantsAsync</c>, via <c>NoStorageKeySource</c> since there's no
/// master key at that point) whenever a peer's server authenticates in for a transit/peer request.
/// </summary>
[TestFixture]
public class PeerCatConversionTests : V2Fixture
{
    private const int MessageFileType = 7030;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task PeerCallFromSam_ConvertsFrodosDepositAboutSam_AndFansOutAppCircleGrant_AndSamCanWrite()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        // The drive Sam will get Write access to on Frodo, once she's a member of circleX (via the
        // app's CircleMemberPermissionGrant) — must exist on Frodo before Sam can write to it.
        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "appDrive", allowAnonymousReads: false);

        // circleX itself carries no drive grants — the deposit is trivial to source (no storage keys
        // needed), and drive access to members comes entirely from the app's CircleMemberPermissionGrant.
        var circleX = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleX, "circleX", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            // A circle must grant at least one drive or one permission — this one carries no drives,
            // so give it a harmless circle-valid permission key instead.
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });

        // A Chat-shaped app on Frodo: ManageCircleMembership to deposit, AuthorizedCircles=[circleX],
        // and a CircleMemberPermissionGrant of Write|React (no Read) on appDrive — mirrors
        // SystemAppConstants.ChatAppRegistrationRequest's pattern.
        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership },
            authorizedCircles: new List<Guid> { circleX },
            circleMemberGrantRequest: new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = appDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                },
                PermissionSet = new PermissionSet()
            });

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleX, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleX), Is.True,
            "precondition: circleX deposit should be pending");
        Assert.That(before.PeerKeyStore.CircleGrants.ContainsKey(circleX), Is.False);
        Assert.That(before.PeerKeyStore.AppGrants.ContainsKey(app.AppId), Is.False);

        // Sam's server calls INTO Frodo's — this is what authenticates Sam via CreateTransitPermissionContextAsync
        // on Frodo's side, converting Frodo's pending deposit about Sam.
        var metadata = SampleMetadataData.Create(fileType: MessageFileType, acl: AccessControlList.Connected,
            allowDistribution: true);
        metadata.AppData.Content = "written via a freshly-converted deposit grant";

        var send = await sam.Drives.PeerWriter.SendUnencryptedFileOverPeer(frodo.Identity, appDrive, metadata);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"write-over-peer send failed: {send.StatusCode}");
        var gtid = send.Content!.RemoteGlobalTransitIdFileIdentifier!.GlobalTransitId;

        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(appDrive);

        // Strongest proof: Sam could actually exercise the Write permission the fanned-out
        // AppCircleGrant provides — the file landed on Frodo's drive.
        var landed = await QueryByGtid(frodo, appDrive, gtid);
        Assert.That(landed, Is.Not.Null, "the file written by Sam should have landed on Frodo's drive");
        Assert.That(landed!.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.CircleGrants.ContainsKey(circleX), Is.True,
            "circleX deposit should have converted into a real CircleGrant");
        Assert.That(after.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleX), Is.False,
            "the deposit should no longer be pending");
        Assert.That(after.PeerKeyStore.AppGrants.TryGetValue(app.AppId, out var appCircleGrants), Is.True,
            "an AppCircleGrant entry should have been fanned out for the app");
        Assert.That(appCircleGrants!.ContainsKey(circleX), Is.True,
            "the fanned-out AppCircleGrant should cover circleX");
    }

    [Test]
    public async Task PeerCall_WithNoPendingDeposits_SucceedsNormally()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "sentinel");

        var metadata = SampleMetadataData.Create(fileType: MessageFileType, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { sam.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        var received = await QueryByGtid(sam, drive, send.Content!.GlobalTransitId!.Value);
        Assert.That(received, Is.Not.Null, "the ordinary peer call should have succeeded with no pending deposits involved");

        var storage = Host.GetTenantScope(sam.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(frodo.Identity);
        Assert.That(icr!.PeerKeyStore.DepositedGrants, Is.Empty,
            "no deposits should exist or be introduced by an ordinary peer call");
    }

    [Test]
    public async Task DepositForCircleDeletedBeforePeerCallArrives_IsSilentlyDropped()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Connect with Sam able to write to Frodo's "trigger" drive — this is what lets Sam's server
        // call into Frodo's later and trigger conversion of whatever Frodo holds pending about Sam.
        var trigger = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "trigger");

        var circleY = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleY, "circleY-doomed", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>(),
            // A circle must grant at least one drive or one permission — this one carries no drives,
            // so give it a harmless circle-valid permission key instead.
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });

        var app = await AppSession.SetupAsync(frodo, trigger, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circleY, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        // The circle has zero real members (only a pending deposit), so it can be deleted outright.
        var deleteResp = await new UniversalCircleNetworkApiClient(frodo.Identity, frodo.Factory).DeleteCircleDefinition(circleY);
        Assert.That(deleteResp.IsSuccessStatusCode, Is.True, $"circle delete failed: {deleteResp.StatusCode}");

        // Sam's server calls into Frodo's, which attempts (and silently drops) the now-dangling deposit.
        var metadata = SampleMetadataData.Create(fileType: MessageFileType, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await sam.Drives.Writer.UploadNewMetadata(trigger.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { frodo.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");

        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(trigger);

        var received = await QueryByGtid(frodo, trigger, send.Content!.GlobalTransitId!.Value);
        Assert.That(received, Is.Not.Null, "the peer call itself must succeed with no error surfacing to the caller");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleY), Is.False,
            "the deposit for the deleted circle should have been dropped");
        Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circleY), Is.False,
            "no CircleGrant should have been created for the deleted circle");
    }

    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader?> QueryByGtid(
        OwnerSession session, TargetDrive drive, Guid gtid)
    {
        var q = await session.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"query failed: {q.StatusCode}");
        return q.Content!.SearchResults.SingleOrDefault();
    }
}
