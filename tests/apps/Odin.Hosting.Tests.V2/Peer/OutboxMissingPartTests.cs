#nullable enable
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// A part missing from the sender's storage is the sender's fault: the outbox gives the item up (UnknownServerError)
/// rather than retrying it as if the recipient were down (RecipientServerNotResponding). Pinned because the payload
/// reads under the outbox became streams in #1892.
/// </summary>
[TestFixture]
public class OutboxMissingPartTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AFileWhoseThumbnailIsMissingFromStorageIsGivenUp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "missing part");

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var response = await frodo.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload],
            new TransitOptions { Recipients = [sam.Identity] });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        var sent = response.Content!;

        // The outbox only runs when drained, so nothing has been sent yet
        var descriptor = (await frodo.Drives.Reader.GetFileHeaderAsync(sent.DriveId, sent.FileId)).Content!
            .FileMetadata.Payloads.Single(p => p.Key == payload.Key);
        var thumb = payload.Thumbnails.Single();
        var thumbPath = TenantPaths(frodo).GetThumbnailDirectoryAndFileName(drive.Alias, sent.FileId, payload.Key,
            descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);
        Assert.That(File.Exists(thumbPath), Is.True, $"precondition: the thumbnail is stored at {thumbPath}");
        File.Delete(thumbPath);

        await PeerFlow.DistributeAsync(frodo, sam, drive);

        await DriveAsserts.AssertTransferStatus(frodo, new ExternalFileIdentifier { FileId = sent.FileId, TargetDrive = drive },
            sam.Identity, LatestTransferStatus.UnknownServerError);
    }
}
