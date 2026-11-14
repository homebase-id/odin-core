#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// A deleted file's payloads and thumbnails went with it: reading one is a 404 that says so (<c>fileDeleted</c>), which a
/// client tells apart from a file that isn't there, a 404 without it.  The file's header still reads, as deleted.
/// </summary>
[TestFixture]
public class DeletedFileReadTests : V2Fixture
{
    [Test]
    public async Task ADeletedFilesPayloadAndThumbnailSayItIsDeletedByFileIdAndGlobalTransitId()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var driveId = spec.TargetDrive.Alias;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var thumb = payload.Thumbnails.First();
        var uniqueId = Guid.NewGuid();

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.OwnerOnly;
        metadata.AppData.UniqueId = uniqueId;
        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(driveId, metadata,
            new UploadManifest { PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList() },
            [payload]);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload: {upload.StatusCode}");
        var fileId = upload.Content!.FileId;
        var gtid = upload.Content.GlobalTransitId!.Value;

        var reader = owner.Drives.Reader;
        Assert.That((await reader.GetPayloadAsync(driveId, fileId, payload.Key)).StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "readable before the delete");

        var delete = await owner.Drives.Writer.SoftDeleteFile(driveId, fileId);
        Assert.That(delete.IsSuccessStatusCode, Is.True, $"delete: {delete.StatusCode} {delete.Error?.Content}");

        DriveAsserts.AssertFileDeleted(await reader.GetPayloadAsync(driveId, fileId, payload.Key), "payload by file id");
        DriveAsserts.AssertFileDeleted(await reader.GetThumbnailAsync(driveId, fileId, payload.Key, thumb.PixelWidth, thumb.PixelHeight),
            "thumbnail by file id");
        DriveAsserts.AssertFileDeleted(await reader.GetPayloadByGtidAsync(gtid, driveId, payload.Key), "payload by global transit id");
        DriveAsserts.AssertFileDeleted(await reader.GetThumbnailByGtidAsync(gtid, driveId, thumb.PixelWidth, thumb.PixelHeight, payload.Key),
            "thumbnail by global transit id");

        // A tombstone keeps no unique id (it may be used again), so nothing answers to it any more: a plain 404
        DriveAsserts.AssertNotFound(await reader.GetPayloadByUniqueIdAsync(uniqueId, driveId, payload.Key), OdinClientErrorCode.NoErrorCode,
            "payload by unique id");

        var header = await reader.GetFileHeaderAsync(driveId, fileId);
        Assert.That((header.StatusCode, header.Content?.FileState), Is.EqualTo((HttpStatusCode.OK, (FileState?)FileState.Deleted)),
            "its header still reads, as deleted");
    }

    [Test]
    public async Task AFileThatIsntThereIsAPlainNotFound()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var (_, owner) = await SetupCallerWithOwner(spec);

        DriveAsserts.AssertNotFound(await owner.Drives.Reader.GetPayloadAsync(spec.TargetDrive.Alias, Guid.NewGuid(), "test_key_1"),
            OdinClientErrorCode.NoErrorCode, "a file that isn't there");
    }
}
