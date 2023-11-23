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
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveGeneralFileTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanUploadMetadataDataWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClient(identity);
        var targetDrive = await ownerApiClient.Drive.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataDataDefinitions.Create(fileType: 100);
        await callerContext.Initialize(ownerApiClient);

        // Act
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);
        
        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");
    }

    [Test]
    public async Task CanUploadFileWith2PayloadsAnd2Thumbnails()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
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
            //Assert.IsTrue(payload.LastModified); //TODO: how to test?
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await client.DriveRedux.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }
        }
    }

    [Test]
    public async Task DeletingFileDeletesAllPayloadsAndThumbnails()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);

        //test the headers payload info
        foreach (var testPayload in testPayloads)
        {
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
            Assert.IsTrue(testPayload.ContentType == payload.ContentType);
            //Assert.IsTrue(payload.LastModified); //TODO: how to test?
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse =
                    await client.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbnailContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbnailContent, thumbnail.Content);
            }
        }


        // Now that we know all are there, let's delete stuff

        var deleteFileResponse = await client.DriveRedux.DeleteFile(uploadResult.File);
        Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
        var result = deleteFileResponse.Content;
        Assert.IsNotNull(result);

        Assert.IsTrue(result.LocalFileDeleted);
        Assert.IsFalse(result.RecipientStatus.Any());

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse =
                    await client.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);
            }
        }
    }

    [Test]
    public async Task CanDeleteByMultipleFileIds()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataDataDefinitions.Create(fileType: 101);
        var f2 = SampleMetadataDataDefinitions.Create(fileType: 202);
        var f3 = SampleMetadataDataDefinitions.Create(fileType: 203);

        UploadResult uploadResult1 = await this.UploadAndValidate(f1, targetDrive.TargetDriveInfo);
        UploadResult uploadResult2 = await this.UploadAndValidate(f2, targetDrive.TargetDriveInfo);
        UploadResult uploadResult3 = await this.UploadAndValidate(f3, targetDrive.TargetDriveInfo);

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

        var deleteListResult = await client.DriveRedux.DeleteFileList(deleteList);
        Assert.IsTrue(deleteListResult.IsSuccessStatusCode);
        var deleteBatchResult = deleteListResult.Content;
        Assert.IsNotNull(deleteBatchResult);

        foreach (var deleteResult in deleteBatchResult.Results)
        {
            Assert.IsTrue(deleteResult.LocalFileDeleted);
            Assert.IsFalse(deleteResult.RecipientStatus.Any());
        }

        foreach (var request in deleteList)
        {
            var getDeletedHeader = await client.DriveRedux.GetFileHeader(request.File);

            Assert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
            Assert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
        }
    }

    [Test]
    public async Task CanDeleteMultipleFilesByGroupIdList()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        var groupId1 = Guid.NewGuid(); // Will delete
        var groupId2 = Guid.NewGuid(); // Will delete
        var groupId3 = Guid.NewGuid(); // Keep this done

        // upload metadata and validate they're uploaded
        var f1 = SampleMetadataDataDefinitions.Create(fileType: 101, groupId: groupId1);
        var f2 = SampleMetadataDataDefinitions.Create(fileType: 202, groupId: groupId1);
        var f3 = SampleMetadataDataDefinitions.Create(fileType: 203, groupId: groupId2);
        var f4 = SampleMetadataDataDefinitions.Create(fileType: 203, groupId: groupId3);

        UploadResult uploadResult1 = await this.UploadAndValidate(f1, targetDrive.TargetDriveInfo);
        UploadResult uploadResult2 = await this.UploadAndValidate(f2, targetDrive.TargetDriveInfo);
        UploadResult uploadResult3 = await this.UploadAndValidate(f3, targetDrive.TargetDriveInfo);
        UploadResult uploadResult4 = await this.UploadAndValidate(f4, targetDrive.TargetDriveInfo);

        //
        // perform the deletes
        //

        var deleteRequests = new List<DeleteFileByGroupIdRequest>()
        {
            new()
            {
                GroupId = groupId1,
                TargetDrive = targetDrive.TargetDriveInfo,
                Recipients = default
            },
            new()
            {
                GroupId = groupId2,
                TargetDrive = targetDrive.TargetDriveInfo,
                Recipients = default
            }
        };

        var deleteFilesByGroupIdListResponse = await client.DriveRedux.DeleteFilesByGroupIdList(new DeleteFilesByGroupIdBatchRequest()
        {
            Requests = deleteRequests
        });

        Assert.IsTrue(deleteFilesByGroupIdListResponse.IsSuccessStatusCode);
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

            var getDeletedHeader = await client.DriveRedux.GetFileHeader(fileDeleteResult.File);

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

            var getDeletedHeader = await client.DriveRedux.GetFileHeader(fileDeleteResult.File);

            Assert.IsTrue(getDeletedHeader.IsSuccessStatusCode);
            Assert.IsTrue(getDeletedHeader.Content.FileState == FileState.Deleted);
        }

        var deletesForGroupId3 = deleteBatchResult.Results.SingleOrDefault(r => r.GroupId == groupId3);
        Assert.IsNull(deletesForGroupId3, "there should be no deletes for group id 3");

        //
        var getHeader = await client.DriveRedux.GetFileHeader(uploadResult4.File);
        Assert.IsTrue(getHeader.IsSuccessStatusCode);
        Assert.IsTrue(getHeader.Content.FileState == FileState.Active);
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