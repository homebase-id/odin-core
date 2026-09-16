using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveArchiveReadWriteTests</c>. An archived drive stays
/// writable by its owner but is closed to every other caller: an app or guest holding a write grant
/// is refused with BadRequest when uploading metadata, uploading a file with payloads and
/// thumbnails, or soft-deleting a file the owner put there.
/// </summary>
/// <remarks>
/// These drive the V1 upload/delete endpoints through the in-process host, so the caller-under-test
/// uses <c>caller.V1.Drive</c> and the owner-side verification uses <c>owner.V1.Drive</c> (the
/// original's <c>DriveRedux</c>).
/// </remarks>
[TestFixture]
public class V1ArchiveReadWriteTests : V2Fixture
{
    /// <summary>
    /// Owner writes to an archived drive succeed; an app or guest is rejected whatever its drive
    /// grant, so the read-only guest row lands on the same BadRequest as the write-granted ones.
    /// </summary>
    public static IEnumerable<object[]> ArchiveCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.BadRequest];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.BadRequest];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.BadRequest];
    }

    [Test, TestCaseSource(nameof(ArchiveCases))]
    public async Task FailToUploadMetadataDataWithoutPayloadsToArchivedDrive(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var setFlagResponse = await owner.Admin.SetArchiveFlag(spec.TargetDrive, true);
        Assert.That(setFlagResponse.IsSuccessStatusCode, Is.True);

        var response = await caller.V1.Drive.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(ArchiveCases))]
    public async Task FailToUploadFileWith2PayloadsAnd2ThumbnailsToArchivedDrive(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var setFlagResponse = await owner.Admin.SetArchiveFlag(spec.TargetDrive, true);
        Assert.That(setFlagResponse.IsSuccessStatusCode, Is.True);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var testPayloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await caller.V1.Drive.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await owner.V1.Drive.GetFileHeader(uploadResult!.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(uploadedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(testPayloads.Count));

        //test the headers payload info
        foreach (var testPayload in testPayloads)
        {
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.That(payload.Thumbnails.Count, Is.EqualTo(testPayload.Thumbnails.Count));
            Assert.That(payload.ContentType, Is.EqualTo(testPayload.ContentType));
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(testPayload.Iv, payload.Iv), Is.True);
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await owner.V1.Drive.GetPayload(uploadResult.File, definition.Key);
            Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders.LastModified!.Value, Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var content = await ReadAllBytesAsync(getPayloadResponse.Content!);
            Assert.That(content, Is.EqualTo(definition.Content));

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await owner.V1.Drive.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
                Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
                Assert.That(getThumbnailResponse.ContentHeaders.LastModified!.Value, Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

                var thumbContent = await ReadAllBytesAsync(getThumbnailResponse.Content!);
                Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
            }
        }
    }

    [Test, TestCaseSource(nameof(ArchiveCases))]
    public async Task FailToDeleteFileOnArchivedDrive(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var setFlagResponse = await owner.Admin.SetArchiveFlag(spec.TargetDrive, true);
        Assert.That(setFlagResponse.IsSuccessStatusCode, Is.True);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var testPayloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await owner.V1.Drive.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Now that we know all are there, let's delete stuff
        var deleteFileResponse = await caller.V1.Drive.SoftDeleteFile(uploadResult!.File);
        Assert.That(deleteFileResponse.StatusCode, Is.EqualTo(expected));

        // Test more if we can
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var result = deleteFileResponse.Content;
        Assert.That(result, Is.Not.Null);

        Assert.That(result!.LocalFileDeleted, Is.True);
        Assert.That(result.RecipientStatus, Is.Empty);

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await owner.V1.Drive.GetPayload(uploadResult.File, definition.Key);
            Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await owner.V1.Drive.GetThumbnail(
                    uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.That(getThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(HttpContent content)
    {
        await using var stream = await content.ReadAsStreamAsync();
        return stream.ToByteArray();
    }
}
