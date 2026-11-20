#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// A recipient changing its own copy of a file it received over transit.
/// </summary>
[TestFixture]
public class ReceivedFileUpdateTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // #2101: the metadata-only update cleared TransitCreated, so a chat dated the message 1970
    [Test]
    public async Task OwnersMetadataUpdate_KeepsTransitCreated()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "received file update");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = [sam.Identity] });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");
        var globalTransitId = send.Content!.GlobalTransitId!.Value;

        await PeerFlow.DistributeAsync(frodo, sam, drive);

        var received = await FindAsync(sam, drive, globalTransitId);
        var transitCreated = received.FileMetadata.TransitCreated.milliseconds;
        Assert.That(transitCreated, Is.GreaterThan(0));

        var updated = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updated.AppData.Content = "opened";
        updated.VersionTag = received.FileMetadata.VersionTag;
        var update = await sam.Drives.Writer.UpdateFileByFileId(drive.Alias, received.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, updated, []);
        Assert.That(update.IsSuccessStatusCode, Is.True, $"update failed: {update.StatusCode}");

        var after = await FindAsync(sam, drive, globalTransitId);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo("opened"));
        Assert.That(after.FileMetadata.TransitCreated.milliseconds, Is.EqualTo(transitCreated));
    }

    private static async Task<SharedSecretEncryptedFileHeader> FindAsync(OwnerSession owner, TargetDrive drive,
        System.Guid globalTransitId)
    {
        var query = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [globalTransitId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true },
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"query failed: {query.StatusCode}");
        return query.Content!.SearchResults.Single();
    }
}
