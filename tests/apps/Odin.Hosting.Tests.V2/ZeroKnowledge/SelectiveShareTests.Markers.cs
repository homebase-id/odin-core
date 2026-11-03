#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Telling someone you shared with them (file-access.md, "Telling someone you shared with them: markers"): Sam's app
/// makes a circle every connection joins, granting Write on his Selective Share drive and nothing else, so Frodo, whom
/// Sam never added to anything, can leave a marker there, once.
/// </summary>
public partial class SelectiveShareTests
{
    private const int MarkerFileType = 0x5ade;

    [TestCase(DrivePermission.Write, CircleGrantOn.Connect, true, TestName = "AnAppMakesACircleEveryConnectionJoinsGrantingWrite")]
    [TestCase(DrivePermission.Read | DrivePermission.Write, CircleGrantOn.Connect, false, TestName = "AnAppsCircleEveryConnectionJoinsCantGrantRead")]
    [TestCase(DrivePermission.Write, CircleGrantOn.Review, false, TestName = "AnAppsCircleDoesntEnrolAtTheOwnersReview")]
    public async Task AnAppsCircleEnrolsByHandOrOnConnect(DrivePermission permission, CircleGrantOn grantOn, bool created)
    {
        var s = await ArrangeAsync();
        var create = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = "anyone may leave me a note", GrantOn = grantOn, DriveGrants = [s.ReadWriteGrant(permission).Drives.Single()]
        });
        Assert.That(create.IsSuccessStatusCode, Is.EqualTo(created), $"{create.StatusCode}: {create.Error?.Content}");
        if (created)
        {
            Assert.That((await s.Frodo.Admin.GetCircleDefinition(create.Content)).GrantOn, Is.EqualTo(grantOn));
        }
    }

    [Test]
    public async Task AMarkerReachesSomeoneWhoNeverAddedTheSenderOnce()
    {
        // Sam's side: his app's Selective Share drive, and its circle every connection joins, granting Write alone.
        // Frodo connected before it existed, so Sam's app enrols the connections it already has, as any app would
        var sams = await ArrangeDriveAsync(await LoginAsOwner(Identities.Sam));
        var network = sams.App.RefitFor<IConnectionNetworkHttpClientApiV2>();
        var create = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "people who share with me", Slug = new OdinSlug("sharers"), GrantOn = CircleGrantOn.Connect,
            DriveGrants = [AppCircles.WriteOn(sams.Drive)]
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        var candidates = await network.GetEnrollmentCandidatesForCircle(create.Content);
        Assert.That(candidates.StatusCode, Is.EqualTo(HttpStatusCode.OK), candidates.Error?.Content);
        Assert.That(candidates.Content!.Candidates.Select(c => c.OdinId.ToString()), Does.Contain(Identities.Frodo),
            "a connection made before the circle");
        var enrol = await network.GrantCircleToMany(new AddManyCircleMembershipRequest
        {
            CircleId = create.Content, OdinIds = candidates.Content.Candidates.Select(c => c.OdinId.ToString()).ToList()
        });
        Assert.That(enrol.StatusCode, Is.EqualTo(HttpStatusCode.OK), enrol.Error?.Content);

        // Frodo's marker, sealed to Sam's drive, its unique id his own: sent again, it is refused, so there is one
        var frodo = await LoginAsOwner(Identities.Frodo);
        var markerId = Guid.NewGuid();
        for (var send = 0; send < 2; send++)
        {
            await ZkTestFiles.SendDepositAsync(frodo, frodo, sams.Owner, sams.WriteOnlyPublicKeyJwk, markerId, "Frodo has files for you",
                remoteDrive: sams.Drive, fileType: MarkerFileType);
        }

        var markers = await sams.Owner.Drives.Reader.GetBatchAsync(sams.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = sams.Drive, FileType = [MarkerFileType] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(markers.StatusCode, Is.EqualTo(HttpStatusCode.OK), markers.Error?.Content);
        var marker = markers.Content!.SearchResults.Single();
        Assert.That((marker.FileMetadata.AppData.UniqueId, marker.FileMetadata.SenderOdinId, marker.SharedSecretEncryptedKeyHeader.Type),
            Is.EqualTo(((Guid?)markerId, Identities.Frodo, EncryptionType.Sealed)), "Frodo's marker, sealed to Sam's drive");
        Assert.That(marker.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Owner), "Sam's alone");
    }

    /// <summary>A zero-knowledge owner's app with a Selective Share drive of its own, which may read the connections.</summary>
    private static async Task<OwnersDrive> ArrangeDriveAsync(OwnerSession owner)
    {
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(owner, OwnerLogin.DefaultPassword);
        var appId = Guid.NewGuid();
        await owner.Admin.EnsureAppRegistered(appId);
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
        await owner.Admin.CreateDrive(drive, "Shared", allowAnonymousReads: false, appId: appId,
            sharingModel: DriveSharingModel.SelectiveShare, zkKeys: keys.Request);
        // ReadConnections: to find the connections made before its circle every connection joins
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.All, [PermissionKeys.ReadConnections], knownAppId: appId);
        return new OwnersDrive(owner, app, drive, keys.WriteOnlyPublicKeyJwk);
    }

    private sealed record OwnersDrive(OwnerSession Owner, AppSession App, TargetDrive Drive, string WriteOnlyPublicKeyJwk);
}
