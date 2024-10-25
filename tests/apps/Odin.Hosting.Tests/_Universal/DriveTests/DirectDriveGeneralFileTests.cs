using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveGeneralFileTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
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
    
    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }
    
    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }
    
    public static IEnumerable WhenGuestOnlyHasReadAccess()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
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
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUploadFileWith2PayloadsAnd2Thumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true, false, false);

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

        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = response.Content;
            Assert.IsNotNull(uploadResult);

            // use the owner api client to validate the file that was uploaded
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

            //test the headers payload info
            foreach (var testPayload in testPayloads)
            {
                var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
                Assert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
                Assert.IsTrue(testPayload.ContentType == payload.ContentType);
                Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(testPayload.Iv, payload.Iv));
                //Assert.IsTrue(payload.LastModified); //TODO: how to test?
            }

            // Get the payloads
            foreach (var definition in testPayloads)
            {
                var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
                Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
                Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(content, definition.Content);

                // Check all the thumbnails
                foreach (var thumbnail in definition.Thumbnails)
                {
                    var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File,
                        thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                    Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                    Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                    Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                    var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                    CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
                }
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
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
        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // Now that we know all are there, let's delete stuff
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(uploadResult.File);
        Assert.IsTrue(deleteFileResponse.StatusCode == expectedStatusCode, $"actual was {deleteFileResponse.StatusCode}");

        // Test more if we can
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var result = deleteFileResponse.Content;
            Assert.IsNotNull(result);

            Assert.IsTrue(result.LocalFileDeleted);
            Assert.IsFalse(result.RecipientStatus.Any());

            // Get the payloads
            foreach (var definition in testPayloads)
            {
                var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
                Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);

                foreach (var thumbnail in definition.Thumbnails)
                {
                    var getThumbnailResponse =
                        await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                    Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);
                }
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanDeleteByMultipleFileIds(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        var f2 = SampleMetadataData.Create(fileType: 202, acl: AccessControlList.Anonymous);
        var f3 = SampleMetadataData.Create(fileType: 203, acl: AccessControlList.Anonymous);

        UploadResult uploadResult1 = await this.UploadAndValidate(f1, targetDrive);
        UploadResult uploadResult2 = await this.UploadAndValidate(f2, targetDrive);
        UploadResult uploadResult3 = await this.UploadAndValidate(f3, targetDrive);

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

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var deleteListResponse = await callerDriveClient.DeleteFileList(deleteList);
        Assert.IsTrue(deleteListResponse.StatusCode == expectedStatusCode,
            $"Status code should be {expectedStatusCode} but was {deleteListResponse.StatusCode}");
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deleteBatchResult = deleteListResponse.Content;
            Assert.IsNotNull(deleteBatchResult);

            foreach (var deleteResult in deleteBatchResult.Results)
            {
                Assert.IsTrue(deleteResult.LocalFileDeleted);
                Assert.IsFalse(deleteResult.RecipientStatus.Any());
            }

            foreach (var request in deleteList)
            {
                var getDeletedHeader = await ownerApiClient.DriveRedux.GetFileHeader(request.File);

                Assert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                Assert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanDeleteMultipleFilesByGroupIdList(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true, false, false);

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

        var deleteRequests = new List<DeleteFileByGroupIdRequest>()
        {
            new()
            {
                GroupId = groupId1,
                TargetDrive = targetDrive,
                Recipients = default
            },
            new()
            {
                GroupId = groupId2,
                TargetDrive = targetDrive,
                Recipients = default
            }
        };

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var deleteFilesByGroupIdListResponse = await uniDriveClient.DeleteFilesByGroupIdList(new DeleteFilesByGroupIdBatchRequest()
        {
            Requests = deleteRequests
        });

        Assert.IsTrue(deleteFilesByGroupIdListResponse.StatusCode == expectedStatusCode,
            $"Status code should be {expectedStatusCode} but was {deleteFilesByGroupIdListResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deleteBatchResult = deleteFilesByGroupIdListResponse.Content;
            Assert.IsNotNull(deleteBatchResult);

            //
            // check group 1
            //

            var deletesForGroupId1 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId1);
            Assert.IsNotNull(deletesForGroupId1);

            Assert.IsTrue(deletesForGroupId1.DeleteFileResults.Count == 2);
            Assert.IsNotNull(deletesForGroupId1.DeleteFileResults.SingleOrDefault(d => d.File == uploadResult1.File));
            Assert.IsNotNull(deletesForGroupId1.DeleteFileResults.SingleOrDefault(d => d.File == uploadResult2.File));

            foreach (var fileDeleteResult in deletesForGroupId1.DeleteFileResults)
            {
                Assert.IsTrue(fileDeleteResult.LocalFileDeleted);
                Assert.IsFalse(fileDeleteResult.RecipientStatus.Any());

                var getDeletedHeader = await ownerApiClient.DriveRedux.GetFileHeader(fileDeleteResult.File);

                Assert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                Assert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }


            //
            // check group 2
            //
            var deletesForGroupId2 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId2);
            Assert.IsNotNull(deletesForGroupId2);


            Assert.IsTrue(deletesForGroupId2.DeleteFileResults.Count == 1);
            Assert.IsNotNull(deletesForGroupId2.DeleteFileResults.SingleOrDefault(d => d.File == uploadResult3.File));

            foreach (var fileDeleteResult in deletesForGroupId2.DeleteFileResults)
            {
                Assert.IsTrue(fileDeleteResult.LocalFileDeleted);
                Assert.IsFalse(fileDeleteResult.RecipientStatus.Any());

                var getDeletedHeader = await ownerApiClient.DriveRedux.GetFileHeader(fileDeleteResult.File);

                Assert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
                Assert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
            }

            var deletesForGroupId3 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId3);
            Assert.IsNull(deletesForGroupId3, "there should be no deletes for group id 3");

            //
            var getHeader = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult4.File);
            Assert.IsTrue(getHeader.IsSuccessStatusCode);
            Assert.IsTrue(getHeader.Content.FileState == FileState.Active);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanGetDeletedFileByGlobalTransitId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);

        var response = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // Now that we know all are there, let's delete stuff
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var deleteFileResponse = await callerDriveClient.SoftDeleteFile(uploadResult.File);
        Assert.IsTrue(deleteFileResponse.StatusCode == expectedStatusCode, $"actual was {deleteFileResponse.StatusCode}");

        // Test more if we can
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var result = deleteFileResponse.Content;
            Assert.IsNotNull(result);

            Assert.IsTrue(result.LocalFileDeleted);
            Assert.IsFalse(result.RecipientStatus.Any());

            var queryBatchResponse = await callerDriveClient.QueryBatch(new QueryBatchRequest()
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

            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode);
            var results = queryBatchResponse.Content.SearchResults;
            var theFile = results.SingleOrDefault();
            Assert.IsNotNull(theFile);
            Assert.IsTrue(theFile.FileState == FileState.Deleted);
        }
    }


    private async Task<UploadResult> UploadAndValidate(UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var response1 = await client.DriveRedux.UploadNewMetadata(targetDrive, f1);
        Assert.IsTrue(response1.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response1.Content!.File);
        Assert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response1.Content;
    }
}