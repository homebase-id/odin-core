using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/FileCleanup/DriveFileUploadTempFilesAreRemovedTests</c>. Uploading
/// and then updating a file with payloads and thumbnails must leave nothing behind: every staging file
/// is gone once the write commits, and an update that replaces one payload, deletes another and clears
/// a third's thumbnails leaves no orphaned payload blobs.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> drive endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
/// The staging-file and orphan checks go through the test-only <c>UploadFileExists</c> /
/// <c>HasOrphanPayloads</c> endpoints, as in the original — nothing here touches the filesystem
/// directly, so the in-process host serves them the same way Kestrel did.
///
/// The two <c>Console.WriteLine</c> calls that printed <c>_scaffold.TestPayloadPath</c> are dropped
/// along with the scaffold; they were diagnostics for a path this framework owns.
///
/// Note the ordering shift <see cref="V2Fixture.SetupCallerWithOwner"/> imposes: the original created
/// the drive, built the metadata, and only then called <c>callerContext.Initialize</c>. Checked — no
/// assertion depends on that order. The original's second <c>callerContext.Initialize</c> call, made
/// mid-test before the update, is likewise dropped: re-initializing registered a second app / guest
/// client and replaced the factory, but the test went on using the client built from the first one.
/// </remarks>
[TestFixture]
public class UploadTempFileCleanupTests : V2Fixture
{
    /// <summary>The original's two stacked case sources, inline: owner and a write-only app.</summary>
    public static IEnumerable<object[]> WriteCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadFileWith2PayloadsAnd2ThumbnailsAndTempFilesAreDeleted(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var testPayloads = new List<TestPayloadDefinition>()
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(uploadedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(testPayloads.Count));

        //
        // verify payloads are in place
        //
        foreach (var definition in testPayloads)
        {
            //test the headers payload info
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == definition.Key);
            Assert.That(payload.Thumbnails.Count, Is.EqualTo(definition.Thumbnails.Count));
            Assert.That(payload.ContentType, Is.EqualTo(definition.ContentType));
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(definition.Iv, payload.Iv), Is.True);

            var getPayloadResponse = await ownerDriveClient.GetPayload(uploadResult.File, definition.Key);
            Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            Assert.That(content, Is.EqualTo(definition.Content));

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await ownerDriveClient.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
                Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
                Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                    Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
            }
        }

        //
        // verify payload temp files are gone
        //
        foreach (var descriptor in header.FileMetadata.Payloads)
        {
            var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
            var payloadStagingFileExistsResponse = await ownerDriveClient.UploadFileExists(
                uploadResult.File, payloadExtension);

            Assert.That(payloadStagingFileExistsResponse.IsSuccessStatusCode, Is.True);
            Assert.That(payloadStagingFileExistsResponse.Content, Is.False);

            foreach (var thumbnail in descriptor.Thumbnails)
            {
                var thumbnailExtension =
                    TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key, descriptor.Uid, thumbnail.PixelWidth,
                        thumbnail.PixelHeight);
                var thumbnailStagingFileExistsResponse = await ownerDriveClient.UploadFileExists(
                    uploadResult.File, thumbnailExtension);

                Assert.That(thumbnailStagingFileExistsResponse.IsSuccessStatusCode, Is.True);
                Assert.That(thumbnailStagingFileExistsResponse.Content, Is.False);
            }
        }
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUpdateFilePayloadsAndThumbnailsAndOrphansAreDeleted(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payloadWithModifiedThumbnails = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payloadThatWillBeDeleted,
            payloadWithModifiedThumbnails
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(uploadedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(testPayloads.Count));

        //
        // Note: I'm skipping verification here since it is done in other tests
        // Now that everything is in place, let's modify the payloads and thumbnails
        //

        var targetFile = uploadResult.File;
        var updatedFileMetadata = uploadedFileMetadata; // no changes to metadata
        updatedFileMetadata.VersionTag = header.FileMetadata.VersionTag;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition2();
        payloadWithModifiedThumbnails.Thumbnails = []; // clear the thumbnails

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payloadThatWillBeDeleted.Key
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadWithModifiedThumbnails.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>() //write empty thumbnails
                    },
                ]
            }
        };

        var updateFileResponse =
            await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd, payloadWithModifiedThumbnails]);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        //
        // verify payload temp files are gone
        //

        // get the latest header of the updated file
        var getUpdatedHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult.File);
        foreach (var descriptor in getUpdatedHeaderResponse.Content!.FileMetadata.Payloads)
        {
            var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
            var payloadStagingFileExistsResponse = await ownerDriveClient.UploadFileExists(
                uploadResult.File, payloadExtension);

            Assert.That(payloadStagingFileExistsResponse.IsSuccessStatusCode, Is.True);
            Assert.That(payloadStagingFileExistsResponse.Content, Is.False);

            foreach (var thumbnail in descriptor.Thumbnails)
            {
                var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                    descriptor.Uid, thumbnail.PixelWidth, thumbnail.PixelHeight);

                var thumbnailStagingFileExistsResponse = await ownerDriveClient.UploadFileExists(
                    uploadResult.File, thumbnailExtension);

                Assert.That(thumbnailStagingFileExistsResponse.IsSuccessStatusCode, Is.True);
                Assert.That(thumbnailStagingFileExistsResponse.Content, Is.False);
            }
        }

        //
        // verify there are no orphans for the deleted payloads and thumbnails
        //
        var hasOrphanPayloadsResponse = await ownerDriveClient.HasOrphanPayloads(targetFile);
        Assert.That(hasOrphanPayloadsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(hasOrphanPayloadsResponse.Content, Is.False);
    }
}
