using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.UnifiedV2.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteNewFileTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveGeneralFileTestsV2
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Pippin]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable TestCasesSecuredDrive()
    {
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };

        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };

        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }


    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUploadMetadataDataWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        await callerContext.Initialize(ownerApiClient);

        // Act
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var driveId = targetDrive.Alias;
        var response = await callerDriveClient.UploadNewMetadata(driveId, uploadedFileMetadata);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUploadFileWith2PayloadsAnd2Thumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var driveId = targetDrive.Alias;
        var response = await callerDriveClient.CreateNewUnencryptedFile(driveId, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var v2Owner = _scaffold.CreateOwnerV2ClientCollection(identity);
            var uploadResult = response.Content;
            ClassicAssert.IsNotNull(uploadResult);

            // use the owner api client to validate the file that was uploaded
            var getHeaderResponse = await v2Owner.DriveReader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

            //test the headers payload info
            foreach (var testPayload in testPayloads)
            {
                var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
                ClassicAssert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
                ClassicAssert.IsTrue(testPayload.ContentType == payload.ContentType);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(testPayload.Iv, payload.Iv));
                //ClassicAssert.IsTrue(payload.LastModified); //TODO: how to test?
            }

            // Get the payloads
            foreach (var definition in testPayloads)
            {
                var getPayloadResponse = await v2Owner.DriveReader.GetPayloadAsync(uploadResult.DriveId, uploadResult.FileId,
                    definition.Key);
                ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(content, definition.Content);

                // Check all the thumbnails
                foreach (var thumbnail in definition.Thumbnails)
                {
                    var getThumbnailResponse = await v2Owner.DriveReader.GetThumbnailAsync(uploadResult.DriveId, uploadResult.FileId,
                        thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                    ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                    ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                    ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                         DateTimeOffset.Now.AddSeconds(10));

                    var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                    CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
                }
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task DeletingFileDeletesAllPayloadsAndThumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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

        var response = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // Now that we know all are there, let's delete stuff
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(callerContext.DriveId, uploadResult.File.FileId);
        ClassicAssert.IsTrue(deleteFileResponse.StatusCode == expectedStatusCode, $"actual was {deleteFileResponse.StatusCode}");

        // Test more if we can
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var result = deleteFileResponse.Content;
            ClassicAssert.IsNotNull(result);

            ClassicAssert.IsTrue(result.LocalFileDeleted);
            ClassicAssert.IsFalse(result.RecipientStatus.Any());

            // Get the payloads
            foreach (var definition in testPayloads)
            {
                var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
                ClassicAssert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);

                foreach (var thumbnail in definition.Thumbnails)
                {
                    var getThumbnailResponse =
                        await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight,
                            definition.Key);
                    ClassicAssert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);
                }
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanDeleteByMultipleFileIds(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var v2Owner = _scaffold.CreateOwnerV2ClientCollection(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        var f2 = SampleMetadataData.Create(fileType: 202, acl: AccessControlList.Anonymous);
        var f3 = SampleMetadataData.Create(fileType: 203, acl: AccessControlList.Anonymous);

        UploadResult uploadResult1 = await this.UploadAndValidate(f1, targetDrive);
        UploadResult uploadResult2 = await this.UploadAndValidate(f2, targetDrive);
        UploadResult uploadResult3 = await this.UploadAndValidate(f3, targetDrive);

        var deleteList = new List<DeleteFileRequestV2>()
        {
            new()
            {
                FileId = uploadResult1.File.FileId,
                Recipients = new List<string>()
            },
            new()
            {
                FileId = uploadResult2.File.FileId,
                Recipients = new List<string>()
            },
            new()
            {
                FileId = uploadResult3.File.FileId,
                Recipients = new List<string>()
            }
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var driveId = callerContext.DriveId;
        var deleteListResponse = await callerDriveClient.DeleteFileList(driveId, deleteList);
        ClassicAssert.IsTrue(deleteListResponse.StatusCode == expectedStatusCode,
            $"Status code should be {expectedStatusCode} but was {deleteListResponse.StatusCode}");
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deleteBatchResult = deleteListResponse.Content;
            ClassicAssert.IsNotNull(deleteBatchResult);

            foreach (var deleteResult in deleteBatchResult.Results)
            {
                ClassicAssert.IsTrue(deleteResult.LocalFileDeleted);
                ClassicAssert.IsFalse(deleteResult.RecipientStatus.Any());
            }

            foreach (var request in deleteList)
            {
                var getDeletedHeader = await v2Owner.DriveReader.GetFileHeaderAsync(driveId, request.FileId);
                ClassicAssert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanDeleteMultipleFilesByGroupIdList(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var v2Owner = _scaffold.CreateOwnerV2ClientCollection(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var groupId1 = Guid.NewGuid(); // Will delete
        var groupId2 = Guid.NewGuid(); // Will delete
        var groupId3 = Guid.NewGuid(); // Keep this done

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataData.Create(fileType: 101, groupId: groupId1, acl: AccessControlList.Anonymous);
        var f2 = SampleMetadataData.Create(fileType: 202, groupId: groupId1, acl: AccessControlList.Anonymous);
        var f3 = SampleMetadataData.Create(fileType: 203, groupId: groupId2, acl: AccessControlList.Anonymous);
        var f4 = SampleMetadataData.Create(fileType: 203, groupId: groupId3, acl: AccessControlList.Anonymous);

        UploadResult uploadResult1 = await this.UploadAndValidate(f1, targetDrive);
        UploadResult uploadResult2 = await this.UploadAndValidate(f2, targetDrive);
        UploadResult uploadResult3 = await this.UploadAndValidate(f3, targetDrive);
        UploadResult uploadResult4 = await this.UploadAndValidate(f4, targetDrive);

        //
        // perform the deletes
        //

        var deleteRequests = new List<DeleteFileByGroupIdRequestV2>()
        {
            new()
            {
                GroupId = groupId1,
                Recipients = default
            },
            new()
            {
                GroupId = groupId2,
                Recipients = default
            }
        };

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var driveId = callerContext.DriveId;
        var deleteFilesByGroupIdListResponse = await uniDriveClient.DeleteFilesByGroupIdList(driveId,
            new DeleteFilesByGroupIdBatchRequestV2()
            {
                Requests = deleteRequests
            });

        ClassicAssert.IsTrue(deleteFilesByGroupIdListResponse.StatusCode == expectedStatusCode,
            $"Status code should be {expectedStatusCode} but was {deleteFilesByGroupIdListResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deleteBatchResult = deleteFilesByGroupIdListResponse.Content;
            ClassicAssert.IsNotNull(deleteBatchResult);

            //
            // check group 1
            //

            var deletesForGroupId1 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId1);
            ClassicAssert.IsNotNull(deletesForGroupId1);

            ClassicAssert.IsTrue(deletesForGroupId1!.DeleteFileResults.Count == 2);
            ClassicAssert.IsNotNull(deletesForGroupId1.DeleteFileResults.SingleOrDefault(d => d.FileId == uploadResult1.File.FileId));
            ClassicAssert.IsNotNull(deletesForGroupId1.DeleteFileResults.SingleOrDefault(d => d.FileId == uploadResult2.File.FileId));

            foreach (var fileDeleteResult in deletesForGroupId1.DeleteFileResults)
            {
                ClassicAssert.IsTrue(fileDeleteResult.LocalFileDeleted);
                ClassicAssert.IsFalse(fileDeleteResult.RecipientStatus.Any());

                var getDeletedHeader = await v2Owner.DriveReader.GetFileHeaderAsync(driveId, fileDeleteResult.FileId);

                ClassicAssert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }


            //
            // check group 2
            //
            var deletesForGroupId2 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId2);
            ClassicAssert.IsNotNull(deletesForGroupId2);
            ClassicAssert.IsTrue(deletesForGroupId2!.DeleteFileResults.Count == 1);
            ClassicAssert.IsNotNull(deletesForGroupId2.DeleteFileResults.SingleOrDefault(d => d.FileId == uploadResult3.File.FileId));

            foreach (var fileDeleteResult in deletesForGroupId2.DeleteFileResults)
            {
                ClassicAssert.IsTrue(fileDeleteResult.LocalFileDeleted);
                ClassicAssert.IsFalse(fileDeleteResult.RecipientStatus.Any());

                var getDeletedHeader = await v2Owner.DriveReader.GetFileHeaderAsync(driveId, fileDeleteResult.FileId);

                ClassicAssert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }

            var deletesForGroupId3 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId3);
            ClassicAssert.IsNull(deletesForGroupId3, "there should be no deletes for group id 3");

            //
            var getHeader = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult4.File);
            ClassicAssert.IsTrue(getHeader.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getHeader.Content.FileState == FileState.Active);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanGetDeletedFileByGlobalTransitId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);

        var response = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // Now that we know all are there, let's delete stuff
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(callerContext.DriveId, uploadResult.File.FileId);
        ClassicAssert.IsTrue(deleteFileResponse.StatusCode == expectedStatusCode, $"actual was {deleteFileResponse.StatusCode}");

        // Test more if we can
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var result = deleteFileResponse.Content;
            ClassicAssert.IsNotNull(result);

            ClassicAssert.IsTrue(result.LocalFileDeleted);
            ClassicAssert.IsFalse(result.RecipientStatus.Any());

            var queryBatchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest()
            {
                QueryParams = new()
                {
                    TargetDrive = targetDrive,
                    GlobalTransitId = []
                },
                ResultOptionsRequest = new()
                {
                    CursorState = null,
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            });

            ClassicAssert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var results = queryBatchResponse.Content.SearchResults;
            var theFile = results.SingleOrDefault();
            ClassicAssert.IsNotNull(theFile);
            ClassicAssert.IsTrue(theFile!.FileState == FileState.Deleted);
        }
    }


    private async Task<UploadResult> UploadAndValidate(UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var response1 = await client.DriveRedux.UploadNewMetadata(targetDrive, f1);
        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response1.Content!.File);
        ClassicAssert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response1.Content;
    }
}