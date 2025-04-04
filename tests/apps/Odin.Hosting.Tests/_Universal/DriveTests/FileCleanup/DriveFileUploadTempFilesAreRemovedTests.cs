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
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.FileCleanup;

public class DriveFileUploadTempFilesAreRemovedTests
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
    public async Task CanUploadMetadataDataWithoutPayloadsAndTempFileIsDeleted(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
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
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // validate metadata is gone
        var uploadResult = response.Content;
        var tempFileExistsResponse = await ownerApiClient.DriveRedux.TempFileExists(uploadResult.File, TempStorageType.Upload, "metadata");
        ClassicAssert.IsTrue(tempFileExistsResponse.IsSuccessStatusCode);
        ClassicAssert.IsFalse(tempFileExistsResponse.Content);
        
        Assert.Inconclusive("TODO: need to handle the package.tempmeatadta file");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUploadFileWith2PayloadsAnd2ThumbnailsAndTempFilesAreDeleted(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        Assert.Inconclusive("TODO");
        
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
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = response.Content;
            ClassicAssert.IsNotNull(uploadResult);

            // use the owner api client to validate the file that was uploaded
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
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
                var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
                ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(content, definition.Content);

                // Check all the thumbnails
                foreach (var thumbnail in definition.Thumbnails)
                {
                    var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File,
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