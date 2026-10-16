using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Create, update and delete answer the file's modified time, so a client that leaves out its own socket events
/// learns its write's time without reading the feed. Each must be the time the header (and so the feed) shows.
/// </summary>
[TestFixture]
public class WriteResponseUpdatedTests : V2Fixture
{
    [Test]
    public async Task EachWriteAnswersTheModifiedTimeItsHeaderShows()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "first", acl: AccessControlList.Anonymous);
        var create = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [] }, []);
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        var file = create.Content!;
        await AssertHeaderShows(owner, file, file.Updated, "create");

        metadata.AppData.Content = "second";
        metadata.VersionTag = file.NewVersionTag;
        var update = await owner.Drives.Writer.UpdateFileByFileId(file.DriveId, file.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        await AssertHeaderShows(owner, file, update.Content!.Updated, "update");

        var delete = await owner.Drives.Writer.SoftDeleteFile(file.DriveId, file.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await AssertHeaderShows(owner, file, delete.Content!.Updated, "delete");
    }

    private static async Task AssertHeaderShows(OwnerSession owner, CreateFileResult file, UnixTimeUtc? answered, string write)
    {
        var header = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"header read after {write}: {header.Error?.Content}");
        Assert.That(answered?.milliseconds, Is.EqualTo(header.Content!.FileMetadata.Updated.milliseconds),
            $"{write} answered updated {answered?.milliseconds}; the header shows {header.Content.FileMetadata.Updated.milliseconds}");
    }
}
