using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// A payload or thumbnail the header names but the store does not have, and that nothing is bringing, is a
/// 410 Gone that no cache keeps, not a server error (#1913): apps show it as unavailable instead of retrying.
/// </summary>
[TestFixture]
public class PayloadGoneTests : V2Fixture
{
    [Test]
    public async Task APayloadAndThumbnailMissingFromTheStoreAreGone()
    {
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "payload-gone");

        var metadata = SampleMetadataData.Create(fileType: 100);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var upload = await owner.V1.Drive.UploadNewFile(targetDrive, metadata,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file = upload.Content!.File;

        var descriptor = (await owner.V1.Drive.GetFileHeader(file)).Content!.FileMetadata.GetPayloadDescriptor(payload.Key)!;
        var thumb = payload.Thumbnails.Single();
        var paths = TenantPaths(owner);
        var payloadPath = paths.GetPayloadDirectoryAndFileName(targetDrive.Alias, file.FileId, payload.Key, descriptor.Uid);
        var thumbPath = paths.GetThumbnailDirectoryAndFileName(targetDrive.Alias, file.FileId, payload.Key, descriptor.Uid,
            thumb.PixelWidth, thumb.PixelHeight);
        Assert.That(File.Exists(payloadPath) && File.Exists(thumbPath), Is.True, "precondition: both are stored on disk");
        File.Delete(payloadPath);
        File.Delete(thumbPath);

        var payloadRead = await owner.V1.Drive.GetPayload(file, payload.Key);
        Assert.That(payloadRead.StatusCode, Is.EqualTo(HttpStatusCode.Gone), "a payload nothing will bring is gone");
        Assert.That(payloadRead.Headers.CacheControl?.NoStore, Is.True, "a cache must not keep it, in case it is restored");

        var thumbRead = await owner.V1.Drive.GetThumbnail(file, thumb.PixelWidth, thumb.PixelHeight, payload.Key);
        Assert.That(thumbRead.StatusCode, Is.EqualTo(HttpStatusCode.Gone), "and so is its thumbnail");
    }
}
