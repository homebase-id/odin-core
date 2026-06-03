using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of the five delete-flow tests from <c>_V2/Tests/Drive/WriteFileTests/DiretDriveWriteNewFileTestsV2</c>:
/// soft-delete a file with payloads, batch delete by FileId list, batch delete by GroupId list (incl.
/// the skip-already-deleted guarantee), and retrieving a deleted file by GlobalTransitId. Owner-uploads
/// the seed data via the V2 writer; the caller-under-test performs the delete via its own factory.
/// The Read-only callers should 403; Write/Owner should succeed.
/// </summary>
[TestFixture]
public class DeleteFileTests : V2Fixture
{
    public static IEnumerable<object[]> CallerCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task DeletingFileDeletesAllPayloadsAndThumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var payloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, manifest, payloads);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"owner seed upload failed: {upload.StatusCode}");

        var result = upload.Content!;
        var deleteResponse = await caller.Drives.Writer.SoftDeleteFile(result.DriveId, result.FileId);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(expected), $"actual {deleteResponse.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        var deleteResult = deleteResponse.Content!;
        Assert.That(deleteResult.LocalFileDeleted, Is.True);
        Assert.That(deleteResult.RecipientStatus, Is.Empty);

        foreach (var def in payloads)
        {
            var getPayload = await owner.Drives.Reader.GetPayloadAsync(result.DriveId, result.FileId, def.Key);
            Assert.That(getPayload.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            foreach (var thumb in def.Thumbnails)
            {
                var getThumb = await owner.Drives.Reader.GetThumbnailAsync(
                    result.DriveId, result.FileId, def.Key, thumb.PixelWidth, thumb.PixelHeight);
                Assert.That(getThumb.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
        }
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task CanDeleteByMultipleFileIds(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var f1 = await UploadMetadata(owner, spec.TargetDrive, 101);
        var f2 = await UploadMetadata(owner, spec.TargetDrive, 202);
        var f3 = await UploadMetadata(owner, spec.TargetDrive, 203);

        var deleteList = new List<DeleteFileRequestV2>
        {
            new() { FileId = f1.FileId, Recipients = new List<string>() },
            new() { FileId = f2.FileId, Recipients = new List<string>() },
            new() { FileId = f3.FileId, Recipients = new List<string>() }
        };

        var response = await caller.Drives.Writer.DeleteFileList(spec.TargetDrive.Alias, deleteList);
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"actual {response.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        var batchResult = response.Content!;
        foreach (var item in batchResult.Results)
        {
            Assert.That(item.LocalFileDeleted, Is.True);
            Assert.That(item.RecipientStatus, Is.Empty);
        }

        foreach (var request in deleteList)
        {
            var header = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, request.FileId);
            Assert.That(header.IsSuccessStatusCode, Is.True);
            Assert.That(header.Content!.FileState, Is.EqualTo(FileState.Deleted));
        }
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task CanDeleteMultipleFilesByGroupIdList(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var groupId1 = Guid.NewGuid();
        var groupId2 = Guid.NewGuid();
        var groupId3 = Guid.NewGuid(); // kept

        var f1 = await UploadMetadata(owner, spec.TargetDrive, 101, groupId1);
        var f2 = await UploadMetadata(owner, spec.TargetDrive, 202, groupId1);
        var f3 = await UploadMetadata(owner, spec.TargetDrive, 203, groupId2);
        var f4 = await UploadMetadata(owner, spec.TargetDrive, 203, groupId3);

        var request = new DeleteFilesByGroupIdBatchRequestV2
        {
            Requests = new List<DeleteFileByGroupIdRequestV2>
            {
                new() { GroupId = groupId1, Recipients = null! },
                new() { GroupId = groupId2, Recipients = null! }
            }
        };

        var response = await caller.Drives.Writer.DeleteFilesByGroupIdList(spec.TargetDrive.Alias, request);
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"actual {response.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        var batchResult = response.Content!;

        var group1Result = batchResult.Results.SingleOrDefault(r => r.GroupId == groupId1);
        Assert.That(group1Result, Is.Not.Null);
        Assert.That(group1Result!.DeleteFileResults.Count, Is.EqualTo(2));
        Assert.That(group1Result.DeleteFileResults.SingleOrDefault(d => d.FileId == f1.FileId), Is.Not.Null);
        Assert.That(group1Result.DeleteFileResults.SingleOrDefault(d => d.FileId == f2.FileId), Is.Not.Null);
        foreach (var fr in group1Result.DeleteFileResults)
        {
            Assert.That(fr.LocalFileDeleted, Is.True);
            Assert.That(fr.RecipientStatus, Is.Empty);
            var header = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, fr.FileId);
            Assert.That(header.Content!.FileState, Is.EqualTo(FileState.Deleted));
        }

        var group2Result = batchResult.Results.SingleOrDefault(r => r.GroupId == groupId2);
        Assert.That(group2Result, Is.Not.Null);
        Assert.That(group2Result!.DeleteFileResults.Count, Is.EqualTo(1));
        Assert.That(group2Result.DeleteFileResults.SingleOrDefault(d => d.FileId == f3.FileId), Is.Not.Null);

        var group3Result = batchResult.Results.SingleOrDefault(r => r.GroupId == groupId3);
        Assert.That(group3Result, Is.Null, "group 3 was not requested for deletion");

        // f4 (in groupId3) should remain active
        var f4Header = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, f4.FileId);
        Assert.That(f4Header.Content!.FileState, Is.EqualTo(FileState.Active));
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task DeleteByGroupIdSkipsAlreadySoftDeletedFiles(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var groupId = Guid.NewGuid();
        var f1 = await UploadMetadata(owner, spec.TargetDrive, 101, groupId);
        var f2 = await UploadMetadata(owner, spec.TargetDrive, 102, groupId);
        var f3 = await UploadMetadata(owner, spec.TargetDrive, 103, groupId);

        // Pre-delete f1 individually first
        var preDelete = await caller.Drives.Writer.SoftDeleteFile(spec.TargetDrive.Alias, f1.FileId);
        if (expected == HttpStatusCode.OK)
        {
            Assert.That(preDelete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var headerAfterFirstDelete = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, f1.FileId);
        var timestampAfterFirstDelete = headerAfterFirstDelete.Content?.FileMetadata?.Updated ?? default;

        var request = new DeleteFilesByGroupIdBatchRequestV2
        {
            Requests = new List<DeleteFileByGroupIdRequestV2>
            {
                new() { GroupId = groupId, Recipients = null! }
            }
        };

        var response = await caller.Drives.Writer.DeleteFilesByGroupIdList(spec.TargetDrive.Alias, request);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        var groupResult = response.Content!.Results.SingleOrDefault(r => r.GroupId == groupId);
        Assert.That(groupResult, Is.Not.Null);
        // Only f2 + f3 should appear — f1 was already deleted
        Assert.That(groupResult!.DeleteFileResults.Count, Is.EqualTo(2));
        Assert.That(groupResult.DeleteFileResults.SingleOrDefault(d => d.FileId == f1.FileId), Is.Null,
            "already-deleted f1 should not appear in results");
        Assert.That(groupResult.DeleteFileResults.SingleOrDefault(d => d.FileId == f2.FileId), Is.Not.Null);
        Assert.That(groupResult.DeleteFileResults.SingleOrDefault(d => d.FileId == f3.FileId), Is.Not.Null);

        // f1's Updated stamp shouldn't have moved
        var f1HeaderAfter = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, f1.FileId);
        Assert.That(f1HeaderAfter.Content!.FileMetadata.Updated, Is.EqualTo(timestampAfterFirstDelete));

        var f2HeaderAfter = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, f2.FileId);
        Assert.That(f2HeaderAfter.Content!.FileState, Is.EqualTo(FileState.Deleted));
        var f3HeaderAfter = await owner.Drives.Reader.GetFileHeaderAsync(spec.TargetDrive.Alias, f3.FileId);
        Assert.That(f3HeaderAfter.Content!.FileState, Is.EqualTo(FileState.Deleted));
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task CanGetDeletedFileByGlobalTransitId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var f = await UploadMetadata(owner, spec.TargetDrive, 100);

        var deleteResponse = await caller.Drives.Writer.SoftDeleteFile(spec.TargetDrive.Alias, f.FileId);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(expected), $"actual {deleteResponse.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        Assert.That(deleteResponse.Content!.LocalFileDeleted, Is.True);
        Assert.That(deleteResponse.Content.RecipientStatus, Is.Empty);

        var queryResponse = await owner.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = spec.TargetDrive,
                GlobalTransitId = []
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                CursorState = null,
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });
        Assert.That(queryResponse.IsSuccessStatusCode, Is.True);
        var theFile = queryResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFile, Is.Not.Null);
        Assert.That(theFile!.FileState, Is.EqualTo(FileState.Deleted));
    }

    private static async Task<CreateFileResult> UploadMetadata(OwnerSession owner, TargetDrive drive, int fileType, Guid? groupId = null)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, groupId: groupId, acl: AccessControlList.Anonymous);
        var response = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner upload failed: {response.StatusCode}");
        return response.Content!;
    }
}
