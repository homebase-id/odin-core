using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.LocalAppMetadata;

public class LocalAppMetadataTests
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

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateLocalAppData(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        // Act
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataRequest
        {
            File = targetFile,
            Tags = [tag1, tag2]
        };

        var response = await callerDriveClient.UpdateLocalAppMetadata(request);
        var result = response.Content;
        Assert.IsFalse(result.NewLocalVersionTag == Guid.Empty);

        // Assert - getting the file should include the metadata
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        var theUpdatedFile = updatedFileResponse.Content;
        // Assert.IsTrue(theUpdatedFile.);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateLocalAppDataWithEmptyTags(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task LocalVersionTagChangesWhenLocalMetadataIsUpdated(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanSearchByLocalAppMetadataTags(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task FailsWithBadRequestWhenFileDoesNotExist(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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
                        await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight,
                            definition.Key);
                    Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);
                }
            }
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