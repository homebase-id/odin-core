using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Reading a payload version that has since been replaced is a client error, not a server one (#1772).
/// </summary>
/// <remarks>
/// A read is two steps -- resolve the header, open the file it names -- and a writer landing between
/// them deletes what the header named. That answered 500 with an <c>OdinSystemException</c>, which told
/// the caller the server had broken when the truth was that the version they asked for was gone.
/// <para>
/// The race itself is what <c>HammerTimeLocalUpdateBatchTests</c> drives, roughly one run in eight.
/// This fixture asks the same question deterministically: hand the read the uid of a payload that has
/// already been replaced, which is exactly the state the losing reader is in, and require the answer
/// that says so.
/// </para>
/// </remarks>
[TestFixture]
public class PayloadVersionGoneTests : V2Fixture
{
    [Test]
    public async Task ReadingAReplacedPayloadVersionSaysItIsGone_RatherThanFailing()
    {
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "version-gone", allowAnonymousReads: true);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var upload = await owner.V1.Drive.UploadNewFile(targetDrive, metadata,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var file = upload.Content!.File;

        var firstHeader = await owner.V1.Drive.GetFileHeader(file);
        var replacedUid = firstHeader.Content!.FileMetadata.Payloads.Single(p => p.Key == payload.Key).Uid;

        // Replace the payload. The file is fine afterwards -- it is the version above that is gone.
        var reupload = await owner.V1.Drive.UploadPayloads(file,
            firstHeader.Content.FileMetadata.VersionTag,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] },
            [payload]);
        Assert.That(reupload.IsSuccessStatusCode, Is.True, $"replacing the payload failed: {reupload.StatusCode}");

        var currentHeader = await owner.V1.Drive.GetFileHeader(file);
        var currentUid = currentHeader.Content!.FileMetadata.Payloads.Single(p => p.Key == payload.Key).Uid;
        Assert.That(currentUid.uniqueTime, Is.Not.EqualTo(replacedUid.uniqueTime),
            "precondition: replacing the payload must move its uid, or there is no stale version to ask for");

        var (scope, context) = await MigrationContextAsync(owner);
        var fs = scope.Resolve<FileSystemResolver>().ResolveFileSystem(FileSystemType.Standard);
        var internalFile = new InternalDriveFileId
        {
            DriveId = targetDrive.Alias,
            FileId = file.FileId
        };
        var thumbnail = payload.Thumbnails.First();

        // The losing reader's position: holding a uid whose file has already been deleted.
        Assert.That(async () => await fs.Storage.GetThumbnailPayloadStreamAsync(
                internalFile, thumbnail.PixelWidth, thumbnail.PixelHeight, payload.Key, replacedUid, context),
            Throws.InstanceOf<OdinPayloadVersionGoneException>(),
            "a version that has been replaced is gone -- a client error, not a server fault");

        // And the current version is still served, so this did not trade one wrong answer for another.
        var current = await owner.V1.Drive.GetThumbnail(file, thumbnail.PixelWidth, thumbnail.PixelHeight, payload.Key);
        Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the version the header names must still read cleanly");
    }
}
