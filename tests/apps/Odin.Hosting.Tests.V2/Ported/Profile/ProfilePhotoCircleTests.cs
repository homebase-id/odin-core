#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// <c>PUT /api/v2/profile/attributes/photo</c> scopes a Connected photo to circles the same way the generic
/// attribute save does.
/// </summary>
[TestFixture]
public class ProfilePhotoCircleTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    private static readonly TargetDrive ProfileDrive = WellKnownAppDrives.ProfileDrive;

    [Test]
    public async Task SetPhoto_ConnectedWithCircle_ReadableByMemberButNotByOtherConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await PeerFlow.ConnectAsync(frodo, sam);
        await PeerFlow.ConnectAsync(frodo, merry);

        var circleId = await CreateCircleAsync(frodo, DrivePermission.Read);
        var grant = await frodo.Connections.GrantCircle(circleId, sam.Identity);
        Assert.That(grant.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"arrange: grant failed: {grant.StatusCode}");

        var response = await new V2ProfileClient(frodo.Identity, frodo.Factory)
            .SetPhotoAttributeAsync(PhotoRequest(circleIds: [circleId]));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");
        var uniqueId = response.Content!.Id;
        var header = (await new DriveReaderV2Client(frodo.Identity, frodo.Factory)
            .GetFileHeaderByUniqueIdAsync(uniqueId, ProfileDrive.Alias)).Content!;
        var fileType = header.FileMetadata.AppData.FileType;

        Assert.That(await ViewAsync(sam, frodo, fileType, uniqueId), Is.Not.Null,
            "a member of the circle should see the photo");
        Assert.That(await ViewAsync(merry, frodo, fileType, uniqueId), Is.Null,
            "a connection outside the circle should not");
    }

    [Test]
    public async Task SetPhoto_CircleIdsWithAnonymous_Returns400()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var circleId = await CreateCircleAsync(frodo, DrivePermission.None);

        var response = await new V2ProfileClient(frodo.Identity, frodo.Factory).SetPhotoAttributeAsync(
            PhotoRequest(ProfileAttributeVisibility.Anonymous, [circleId]));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"actual {response.StatusCode}");
    }

    [Test]
    public async Task SetPhoto_UnknownCircleId_Returns400()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var response = await new V2ProfileClient(frodo.Identity, frodo.Factory).SetPhotoAttributeAsync(
            PhotoRequest(circleIds: [Guid.NewGuid()]));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"actual {response.StatusCode}");
    }

    [Test]
    public async Task SetPhoto_Overwrite_ReplacesTheCircleList()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var profile = new V2ProfileClient(frodo.Identity, frodo.Factory);
        var first = await CreateCircleAsync(frodo, DrivePermission.None);
        var second = await CreateCircleAsync(frodo, DrivePermission.None);

        var created = await profile.SetPhotoAttributeAsync(PhotoRequest(circleIds: [first]));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {created.StatusCode}");
        Assert.That((await AclOf(frodo, created.Content!.Id)).CircleIdList, Is.EqualTo(new List<Guid> { first }));

        var request = PhotoRequest(circleIds: [second]);
        request.Id = created.Content.Id;
        request.ExpectedVersionTag = created.Content.VersionTag;
        var updated = await profile.SetPhotoAttributeAsync(request);
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {updated.StatusCode}");

        var acl = await AclOf(frodo, created.Content.Id);
        Assert.That(acl.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected));
        Assert.That(acl.CircleIdList, Is.EqualTo(new List<Guid> { second }), "replaced, not merged");
    }

    private static SetPhotoAttributeRequest PhotoRequest(
        ProfileAttributeVisibility visibility = ProfileAttributeVisibility.Connected, List<Guid>? circleIds = null) => new()
    {
        Visibility = visibility,
        CircleIds = circleIds,
        ContentType = "image/webp",
        Content = Enumerable.Repeat((byte)0xAB, 128).ToArray(),
        Thumbnails =
        [
            new ProfilePhotoThumbnail
            {
                PixelWidth = 250,
                PixelHeight = 250,
                ContentType = "image/jpeg",
                Content = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()
            }
        ]
    };

    private static async Task<Guid> CreateCircleAsync(OwnerSession owner, DrivePermission permission)
    {
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Friends",
            TestUtils.CreatePermissionGrantRequest(ProfileDrive, permission));
        return circleId;
    }

    private static async Task<AccessControlList> AclOf(OwnerSession owner, Guid uniqueId)
    {
        var header = (await new DriveReaderV2Client(owner.Identity, owner.Factory)
            .GetFileHeaderByUniqueIdAsync(uniqueId, ProfileDrive.Alias)).Content!;
        return header.ServerMetadata.AccessControlList;
    }

    private static async Task<SharedSecretEncryptedFileHeader?> ViewAsync(OwnerSession viewer, OwnerSession frodo,
        int fileType, Guid uniqueId)
    {
        var response = await viewer.RefitFor<IUniversalRefitPeerQuery>().GetBatch(new PeerQueryBatchRequest
        {
            OdinId = frodo.Identity,
            QueryParams = new FileQueryParamsV1 { TargetDrive = ProfileDrive, FileType = [fileType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query failed: {response.StatusCode}");
        return response.Content!.SearchResults.SingleOrDefault(h => h.FileMetadata.AppData.UniqueId == uniqueId);
    }
}
