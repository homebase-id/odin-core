using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/UpdateBatch/DirectDriveLocalUpdateBatchTests</c>. Covers the
/// update-batch ("update file") call with <see cref="UpdateLocale.Local"/> — i.e. no peer
/// recipients — across the caller matrix: updating a header-only file, updating a file while
/// appending one payload and deleting another, the orphan-thumbnail case (a payload keeps its
/// content but loses one thumbnail), and addressing the file to update by
/// <c>UniqueId</c> and by <c>GlobalTransitId</c> instead of by file id.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> drive endpoints (<c>/api/owner/v1/drive/files/update</c>) through the
/// in-process host via the V1-shaped <c>UniversalDriveApiClient</c>, reached here through
/// <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>. <c>UpdateBatchTests</c> in this same folder covers
/// the V2-endpoint variant; hence the <c>V1</c> prefix here.
///
/// Nothing here is peer-dependent — every instruction set sets <c>Locale = UpdateLocale.Local</c> and
/// a default <c>Recipients</c> — so the fixture runs on the default single host identity rather than
/// the original's Pippin. The full fixture is ported; no test was left behind.
/// </remarks>
[TestFixture]
public class V1LocalUpdateBatchTests : V2Fixture
{
    /// <summary>
    /// The original's four stacked case sources, inline. Update-batch writes to the drive, so owner
    /// and a write-only app succeed; a guest is refused whether it holds write or only read, because
    /// the update endpoint is not exposed to the guest surface at all.
    /// </summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadNewFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var callerDriveClient = caller.V1.Drive;
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchWith1PayloadsAnd1Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillBeDeleted.ToPayloadDescriptor()]
        };

        var uploadNewFileResponse = await ownerDriveClient.UploadNewFile(spec.TargetDrive,
            uploadedFileMetadata, uploadManifest, [payloadThatWillBeDeleted]);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;
        updatedFileMetadata.VersionTag = uploadResult!.NewVersionTag;

        var targetFile = uploadNewFileResponse.Content!.File;
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition2();

        // create instruction set
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
                    }
                ]
            }
        };

        var callerDriveClient = caller.V1.Drive;
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Not.Contain(payloadThatWillBeDeleted.Key),
            "payload 1 should have been removed");
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToAdd.Key),
            "payloadToAdd should have been, well, added :)");

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await ownerDriveClient.GetPayload(targetFile, payloadToAdd.Key);
        Assert.That(getPayloadToAddResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        var content = (await getPayloadToAddResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        Assert.That(content, Is.EqualTo(payloadToAdd.Content));

        // Check all the thumbnails
        foreach (var thumbnail in payloadToAdd.Thumbnails)
        {
            var getThumbnailResponse = await ownerDriveClient.GetThumbnail(targetFile, thumbnail.PixelWidth,
                thumbnail.PixelHeight, payloadToAdd.Key);

            Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent = (await getThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
        }

        //
        // Ensure we get 404 for the payload1
        //
        var getPayload1Response = await ownerDriveClient.GetPayload(targetFile, payloadThatWillBeDeleted.Key);
        Assert.That(getPayload1Response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        //
        // Ensure we find the file on the recipient
        //
        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchWith1PayloadsAnd1ThumbnailsHandleOrphanThumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var payloadThatWillLoseAThumbnail = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var thumbnailToBeDeleted = new ThumbnailContent
        {
            PixelWidth = 140,
            PixelHeight = 140,
            ContentType = "image/jpg",
            Content = "some thumbnail content".ToUtf8ByteArray()
        };

        payloadThatWillLoseAThumbnail.Thumbnails.Add(thumbnailToBeDeleted);

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillLoseAThumbnail.ToPayloadDescriptor()]
        };

        var uploadNewFileResponse = await ownerDriveClient.UploadNewFile(spec.TargetDrive,
            uploadedFileMetadata, uploadManifest, [payloadThatWillLoseAThumbnail]);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;
        updatedFileMetadata.VersionTag = uploadResult!.NewVersionTag;

        var targetFile = uploadNewFileResponse.Content!.File;
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition2();


        payloadThatWillLoseAThumbnail.Thumbnails.RemoveAll(t =>
            t.PixelHeight == thumbnailToBeDeleted.PixelHeight &&
            t.PixelWidth == thumbnailToBeDeleted.PixelWidth);

        // create instruction set
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
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadThatWillLoseAThumbnail.Key,
                        DescriptorContent = null,
                        ContentType = payloadThatWillLoseAThumbnail.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = payloadThatWillLoseAThumbnail.Thumbnails // all but the one to be removed
                            .Select(thumb => new UploadedManifestThumbnailDescriptor
                            {
                                ThumbnailKey =
                                    $"{payloadThatWillLoseAThumbnail.Key}{thumb.PixelWidth}{thumb.PixelHeight}", //hulk smash (it all together)
                                PixelWidth = thumb.PixelWidth,
                                PixelHeight = thumb.PixelHeight,
                                ContentType = thumb.ContentType,
                            })
                    }
                ]
            }
        };

        var callerDriveClient = caller.V1.Drive;
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata,
        [
            payloadToAdd,
            payloadThatWillLoseAThumbnail
        ]);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(2));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadThatWillLoseAThumbnail.Key));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(payloadToAdd.Key),
            "payloadToAdd should have been, well, added :)");

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await ownerDriveClient.GetPayload(targetFile, payloadToAdd.Key);
        Assert.That(getPayloadToAddResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        var content = (await getPayloadToAddResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        Assert.That(content, Is.EqualTo(payloadToAdd.Content));

        // Check all the thumbnails
        foreach (var thumbnail in payloadToAdd.Thumbnails)
        {
            var getThumbnailResponse = await ownerDriveClient.GetThumbnail(targetFile, thumbnail.PixelWidth,
                thumbnail.PixelHeight, payloadToAdd.Key);

            Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent = (await getThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
        }

        //
        // payloadThatWillLoseAThumbnail should still be on the server but not have the thumbnailToBeDeleted
        //
        var getPayloadThatWillLoseAThumbnailResponse =
            await ownerDriveClient.GetPayload(targetFile, payloadThatWillLoseAThumbnail.Key);
        Assert.That(getPayloadThatWillLoseAThumbnailResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadThatWillLoseAThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadThatWillLoseAThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        var payloadThatWillLoseAThumbnailContent =
            (await getPayloadThatWillLoseAThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        Assert.That(payloadThatWillLoseAThumbnailContent, Is.EqualTo(payloadThatWillLoseAThumbnail.Content));

        // Check all the thumbnails
        foreach (var thumbnail in payloadThatWillLoseAThumbnail.Thumbnails)
        {
            var getThumbnailResponseForPayloadThatWillLoseAThumbnail = await ownerDriveClient.GetThumbnail(targetFile,
                thumbnail.PixelWidth,
                thumbnail.PixelHeight, payloadThatWillLoseAThumbnail.Key);

            Assert.That(getThumbnailResponseForPayloadThatWillLoseAThumbnail.IsSuccessStatusCode, Is.True);
            Assert.That(getThumbnailResponseForPayloadThatWillLoseAThumbnail.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponseForPayloadThatWillLoseAThumbnail.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent =
                (await getThumbnailResponseForPayloadThatWillLoseAThumbnail.Content!.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
        }

        //
        // Get a 404 for the thumbnailToBeDeleted
        //
        var getThumbnailToBeDeletedResponse = await ownerDriveClient.GetThumbnail(targetFile, thumbnailToBeDeleted.PixelWidth,
            thumbnailToBeDeleted.PixelHeight, payloadThatWillLoseAThumbnail.Key, directMatchOnly: true);
        Assert.That(getThumbnailToBeDeletedResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        //
        // Ensure we find the file on the recipient
        //
        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchByIdentifyingFileWithUniqueIdWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.UniqueId = Guid.NewGuid();
        var uploadNewFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = new FileIdentifier()
            {
                UniqueId = uploadedFileMetadata.AppData.UniqueId,
                TargetDrive = spec.TargetDrive
            },
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var callerDriveClient = caller.V1.Drive;
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchByIdentifyingFileWithGlobalTransitIdWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.UniqueId = Guid.NewGuid();
        var uploadNewFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = new FileIdentifier()
            {
                GlobalTransitId = uploadResult.GlobalTransitId,
                TargetDrive = spec.TargetDrive
            },
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var callerDriveClient = caller.V1.Drive;
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }
}
