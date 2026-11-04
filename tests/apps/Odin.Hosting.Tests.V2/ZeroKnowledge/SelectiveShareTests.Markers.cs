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
using Odin.Services.Apps;
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

    [Test]
    public async Task AMarkerReachesSomeoneWhoNeverAddedTheSenderOnce()
    {
        // Sam's side: his app's Selective Share drive, and its circle every connection joins, granting Write alone.
        // Frodo connected before it existed, so Sam's app enrols the connections it already has, as any app would
        // ReadConnections: to find the connections made before its circle every connection joins
        var sams = await ArrangeDriveAsync(await LoginAsOwner(Identities.Sam), PermissionKeys.ReadConnections);
        var network = sams.App.RefitFor<IConnectionNetworkHttpClientApiV2>();
        var create = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "people who share with me", Slug = new OdinSlug("sharers"), GrantOn = CircleGrantOn.Connect,
            DriveGrants = [AppCircles.WriteOn(sams.Drive)]
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        Assert.That((await sams.Owner.Admin.GetCircleDefinition(create.Content)).GrantOn, Is.EqualTo(CircleGrantOn.Connect));
        var candidates = await network.GetEnrollmentCandidatesForCircle(create.Content);
        Assert.That(candidates.StatusCode, Is.EqualTo(HttpStatusCode.OK), candidates.Error?.Content);
        Assert.That(candidates.Content!.Candidates.Select(c => c.OdinId.ToString()), Does.Contain(Identities.Frodo),
            "a connection made before the circle");
        var enrol = await network.GrantCircleToMany(new AddManyCircleMembershipRequest
        {
            CircleId = create.Content, OdinIds = candidates.Content.Candidates.Select(c => c.OdinId.ToString()).ToList()
        });
        Assert.That(enrol.StatusCode, Is.EqualTo(HttpStatusCode.OK), enrol.Error?.Content);

        // Frodo's marker, sealed to Sam's drive, its unique id his own
        var frodo = await LoginAsOwner(Identities.Frodo);
        var markerId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(frodo, frodo, sams.Owner, sams.WriteOnlyPublicKeyJwk, markerId, "Frodo has files for you",
            remoteDrive: sams.Drive, fileType: MarkerFileType);

        var marker = await MarkerAsync(sams, markerId);
        Assert.That(marker.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Owner), "Sam's alone");

        // Sent again: Frodo updates it, though he can't read it, as no one else has written it since (docs/file-access.md,
        // "Changing a file you sent"); still one marker
        await ZkTestFiles.SendDepositAsync(frodo, frodo, sams.Owner, sams.WriteOnlyPublicKeyJwk, markerId, "Frodo has new files for you",
            remoteDrive: sams.Drive, fileType: MarkerFileType);
        var updated = await MarkerAsync(sams, markerId);
        Assert.That(updated.FileMetadata.VersionTag, Is.Not.EqualTo(marker.FileMetadata.VersionTag), "updated");
        Assert.That(updated.FileMetadata.AppData.Content, Is.Not.EqualTo(marker.FileMetadata.AppData.Content), "with the new content");
    }

    /// <summary>The one marker on Sam's drive: Frodo's, sealed to Sam's drive.</summary>
    private static async Task<SharedSecretEncryptedFileHeader> MarkerAsync(OwnersDrive sams, Guid markerId)
    {
        var markers = await sams.Owner.Drives.Reader.GetBatchAsync(sams.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = sams.Drive, FileType = [MarkerFileType] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(markers.StatusCode, Is.EqualTo(HttpStatusCode.OK), markers.Error?.Content);
        var marker = markers.Content!.SearchResults.Single();
        Assert.That((marker.FileMetadata.AppData.UniqueId, marker.FileMetadata.SenderOdinId, marker.SharedSecretEncryptedKeyHeader.Type),
            Is.EqualTo(((Guid?)markerId, Identities.Frodo, EncryptionType.Sealed)), "Frodo's marker, sealed to Sam's drive");
        return marker;
    }

    /// <summary>A zero-knowledge owner's app, holding <paramref name="permissionKeys"/>, with a Selective Share drive of its own.</summary>
    private static async Task<OwnersDrive> ArrangeDriveAsync(OwnerSession owner, params int[] permissionKeys)
    {
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(owner, OwnerLogin.DefaultPassword);
        var appId = Guid.NewGuid();
        await owner.Admin.EnsureAppRegistered(appId);
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
        await owner.Admin.CreateDrive(drive, "Shared", allowAnonymousReads: false, appId: appId,
            sharingModel: DriveSharingModel.SelectiveShare, zkKeys: keys.Request);
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.All, permissionKeys, knownAppId: appId);
        return new OwnersDrive(owner, app, drive, keys.DriveKey, keys.WriteOnlyPublicKeyJwk);
    }

    private sealed record OwnersDrive(OwnerSession Owner, AppSession App, TargetDrive Drive, byte[] DriveKey, string WriteOnlyPublicKeyJwk);
}
