using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Verifies the authorization policy on the peer endpoints (`[UnifiedV2Authorize(OwnerOrApp)]`): an App
/// caller holding <see cref="PermissionKeys.UseTransitRead"/> can read over peer, while a Guest
/// (YouAuth) caller is rejected. Owner is already covered by the other peer fixtures.
/// </summary>
[TestFixture]
public class PeerAuthMatrixTests : V2Fixture
{
    private const int CommunityMessageFileType = 7020;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppCaller_WithUseTransitRead_CanQueryAndReadOverPeer()
    {
        var member = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(member, owner, DrivePermission.Read, "community",
            allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: CommunityMessageFileType, acl: AccessControlList.Connected);
        metadata.AppData.Content = "hi from owner";
        var upload = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"owner upload failed: {upload.StatusCode}");

        // App registered on the member, granted drive Read + the UseTransitRead permission key.
        var app = await AppSession.SetupAsync(member, drive, DrivePermission.Read,
            permissionKeys: new List<int> { PermissionKeys.UseTransitRead });

        var query = await app.Drives.Peer.QueryBatchAsync(owner.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True,
            $"App caller with UseTransitRead should query over peer; got {query.StatusCode}");
        var fileId = query.Content!.SearchResults.Single().FileId;

        var header = await app.Drives.Peer.GetFileHeaderAsync(owner.Identity, drive.Alias, fileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"App caller should read the header over peer; got {header.StatusCode}");
    }

    [Test]
    public async Task GuestCaller_IsRejectedFromPeerQuery()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "community", allowAnonymousReads: false);

        var guest = await GuestSession.SetupAsync(owner, drive, DrivePermission.Read);

        var response = await guest.Drives.Peer.QueryBatchAsync((OdinId)Identities.Sam, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { CommunityMessageFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        });

        Assert.That(response.IsSuccessStatusCode, Is.False,
            $"Guest/YouAuth caller must be rejected by the OwnerOrApp policy; got {response.StatusCode}");
    }
}
