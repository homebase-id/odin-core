using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveGeneralFileTests</c>. Covers the general (non-security)
/// drive file features driven directly against an identity's own drives: uploading header-only
/// metadata, uploading a file with two payloads and their thumbnails, soft-deleting a file (and
/// confirming its payloads and thumbnails go with it), the two batch-delete shapes (by file id and by
/// group id), and querying a deleted file back by global transit id — each across the caller matrix.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> drive endpoints (<c>/api/owner/v1/drive/files/...</c>) through the
/// in-process host via the V1-shaped <see cref="UniversalDriveApiClient"/>, reached here through
/// <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
///
/// Nothing here is peer-dependent: the two batch-delete requests carry an empty / default
/// <c>Recipients</c> list and no upload sets <c>TransitOptions</c>, so every case is local to the one
/// identity and the fixture runs on the default single host identity rather than the original's Pippin.
/// The full fixture is ported; no test was left behind.
/// </remarks>
[TestFixture]
public class V1GeneralFileTests : V2Fixture
{
    /// <summary>
    /// The original's four stacked case sources, inline. Writing to the drive needs a write grant, so
    /// owner, a write-only app and a write-only guest all succeed while a read-only guest is refused.
    /// </summary>
    public static IEnumerable<object[]> WriteCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadMetadataDataWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        // Setup
        var caller = await SetupCaller(spec);
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        // Act
        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadFileWith2PayloadsAnd2Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

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

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // use the owner api client to validate the file that was uploaded
        var ownerDriveClient = owner.V1.Drive;
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult!.File);
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
            var getPayloadResponse = await ownerDriveClient.GetPayload(uploadResult.File, definition.Key);
            Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var content = (await getPayloadResponse.Content!.ReadAsStreamAsync()).ToByteArray();
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

                var thumbContent = (await getThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
                Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
            }
        }
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task DeletingFileDeletesAllPayloadsAndThumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var testPayloads = new List<TestPayloadDefinition>()
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Now that we know all are there, let's delete stuff
        var callerDriveClient = caller.V1.Drive;

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(uploadResult!.File);
        Assert.That(deleteFileResponse.StatusCode, Is.EqualTo(expected));

        // Test more if we can
        if (expected != HttpStatusCode.OK) return;

        var result = deleteFileResponse.Content;
        Assert.That(result, Is.Not.Null);

        Assert.That(result!.LocalFileDeleted, Is.True);
        Assert.That(result.RecipientStatus, Is.Empty);

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await ownerDriveClient.GetPayload(uploadResult.File, definition.Key);
            Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await ownerDriveClient.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.That(getThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
        }
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanDeleteByMultipleFileIds(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        var f2 = SampleMetadataData.Create(fileType: 202, acl: AccessControlList.Anonymous);
        var f3 = SampleMetadataData.Create(fileType: 203, acl: AccessControlList.Anonymous);

        var uploadResult1 = await UploadAndValidate(ownerDriveClient, f1, spec.TargetDrive);
        var uploadResult2 = await UploadAndValidate(ownerDriveClient, f2, spec.TargetDrive);
        var uploadResult3 = await UploadAndValidate(ownerDriveClient, f3, spec.TargetDrive);

        var deleteList = new List<DeleteFileRequest>()
        {
            new()
            {
                File = uploadResult1.File,
                Recipients = new List<string>()
            },
            new()
            {
                File = uploadResult2.File,
                Recipients = new List<string>()
            },
            new()
            {
                File = uploadResult3.File,
                Recipients = new List<string>()
            }
        };

        var callerDriveClient = caller.V1.Drive;

        var deleteListResponse = await callerDriveClient.DeleteFileList(deleteList);
        Assert.That(deleteListResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        var deleteBatchResult = deleteListResponse.Content;
        Assert.That(deleteBatchResult, Is.Not.Null);

        foreach (var deleteResult in deleteBatchResult!.Results)
        {
            Assert.That(deleteResult.LocalFileDeleted, Is.True);
            Assert.That(deleteResult.RecipientStatus, Is.Empty);
        }

        foreach (var request in deleteList)
        {
            var getDeletedHeader = await ownerDriveClient.GetFileHeader(request.File);

            Assert.That(getDeletedHeader.IsSuccessStatusCode, Is.True);
            Assert.That(getDeletedHeader.Content!.FileState, Is.EqualTo(FileState.Deleted));
        }
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanDeleteMultipleFilesByGroupIdList(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var groupId1 = Guid.NewGuid(); // Will delete
        var groupId2 = Guid.NewGuid(); // Will delete
        var groupId3 = Guid.NewGuid(); // Keep this done

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataData.Create(fileType: 101, groupId: groupId1, acl: AccessControlList.Anonymous);
        var f2 = SampleMetadataData.Create(fileType: 202, groupId: groupId1, acl: AccessControlList.Anonymous);
        var f3 = SampleMetadataData.Create(fileType: 203, groupId: groupId2, acl: AccessControlList.Anonymous);
        var f4 = SampleMetadataData.Create(fileType: 203, groupId: groupId3, acl: AccessControlList.Anonymous);

        var uploadResult1 = await UploadAndValidate(ownerDriveClient, f1, spec.TargetDrive);
        var uploadResult2 = await UploadAndValidate(ownerDriveClient, f2, spec.TargetDrive);
        var uploadResult3 = await UploadAndValidate(ownerDriveClient, f3, spec.TargetDrive);
        var uploadResult4 = await UploadAndValidate(ownerDriveClient, f4, spec.TargetDrive);

        //
        // perform the deletes
        //

        var deleteRequests = new List<DeleteFileByGroupIdRequest>()
        {
            new()
            {
                GroupId = groupId1,
                TargetDrive = spec.TargetDrive,
                Recipients = default
            },
            new()
            {
                GroupId = groupId2,
                TargetDrive = spec.TargetDrive,
                Recipients = default
            }
        };

        var uniDriveClient = caller.V1.Drive;

        var deleteFilesByGroupIdListResponse = await uniDriveClient.DeleteFilesByGroupIdList(new DeleteFilesByGroupIdBatchRequest()
        {
            Requests = deleteRequests
        });

        Assert.That(deleteFilesByGroupIdListResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        var deleteBatchResult = deleteFilesByGroupIdListResponse.Content;
        Assert.That(deleteBatchResult, Is.Not.Null);

        //
        // check group 1
        //

        var deletesForGroupId1 = deleteBatchResult!.Results.SingleOrDefault(r => r.GroupId == groupId1);
        Assert.That(deletesForGroupId1, Is.Not.Null);

        Assert.That(deletesForGroupId1!.DeleteFileResults.Count, Is.EqualTo(2));
        Assert.That(deletesForGroupId1.DeleteFileResults,
            Has.Exactly(1).Matches<DeleteFileResult>(d => d.File == uploadResult1.File));
        Assert.That(deletesForGroupId1.DeleteFileResults,
            Has.Exactly(1).Matches<DeleteFileResult>(d => d.File == uploadResult2.File));

        foreach (var fileDeleteResult in deletesForGroupId1.DeleteFileResults)
        {
            Assert.That(fileDeleteResult.LocalFileDeleted, Is.True);
            Assert.That(fileDeleteResult.RecipientStatus, Is.Empty);

            var getDeletedHeader = await ownerDriveClient.GetFileHeader(fileDeleteResult.File);

            Assert.That(getDeletedHeader.IsSuccessStatusCode, Is.True);
            Assert.That(getDeletedHeader.Content!.FileState, Is.EqualTo(FileState.Deleted));
        }

        //
        // check group 2
        //
        var deletesForGroupId2 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId2);
        Assert.That(deletesForGroupId2, Is.Not.Null);

        Assert.That(deletesForGroupId2!.DeleteFileResults.Count, Is.EqualTo(1));
        Assert.That(deletesForGroupId2.DeleteFileResults,
            Has.Exactly(1).Matches<DeleteFileResult>(d => d.File == uploadResult3.File));

        foreach (var fileDeleteResult in deletesForGroupId2.DeleteFileResults)
        {
            Assert.That(fileDeleteResult.LocalFileDeleted, Is.True);
            Assert.That(fileDeleteResult.RecipientStatus, Is.Empty);

            var getDeletedHeader = await ownerDriveClient.GetFileHeader(fileDeleteResult.File);

            Assert.That(getDeletedHeader.IsSuccessStatusCode, Is.True);
            Assert.That(getDeletedHeader.Content!.FileState, Is.EqualTo(FileState.Deleted));
        }

        var deletesForGroupId3 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId3);
        Assert.That(deletesForGroupId3, Is.Null, "there should be no deletes for group id 3");

        //
        var getHeader = await ownerDriveClient.GetFileHeader(uploadResult4.File);
        Assert.That(getHeader.IsSuccessStatusCode, Is.True);
        Assert.That(getHeader.Content!.FileState, Is.EqualTo(FileState.Active));
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanGetDeletedFileByGlobalTransitId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);

        var response = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Now that we know all are there, let's delete stuff
        var callerDriveClient = caller.V1.Drive;

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(uploadResult!.File);
        Assert.That(deleteFileResponse.StatusCode, Is.EqualTo(expected));

        // Test more if we can
        if (expected != HttpStatusCode.OK) return;

        var result = deleteFileResponse.Content;
        Assert.That(result, Is.Not.Null);

        Assert.That(result!.LocalFileDeleted, Is.True);
        Assert.That(result.RecipientStatus, Is.Empty);

        var queryBatchResponse = await callerDriveClient.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = new()
            {
                TargetDrive = spec.TargetDrive,
                GlobalTransitId = []
            },
            ResultOptionsRequest = new()
            {
                CursorState = null,
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

        Assert.That(queryBatchResponse.IsSuccessStatusCode, Is.True);
        var results = queryBatchResponse.Content!.SearchResults;
        var theFile = results.SingleOrDefault();
        Assert.That(theFile, Is.Not.Null);
        Assert.That(theFile!.FileState, Is.EqualTo(FileState.Deleted));
    }

    private static async Task<UploadResult> UploadAndValidate(
        UniversalDriveApiClient ownerDriveClient,
        UploadFileMetadata f1,
        TargetDrive targetDrive)
    {
        var response1 = await ownerDriveClient.UploadNewMetadata(targetDrive, f1);
        Assert.That(response1.IsSuccessStatusCode, Is.True);
        var getHeaderResponse1 = await ownerDriveClient.GetFileHeader(response1.Content!.File);
        Assert.That(getHeaderResponse1.IsSuccessStatusCode, Is.True);
        return response1.Content;
    }
}
