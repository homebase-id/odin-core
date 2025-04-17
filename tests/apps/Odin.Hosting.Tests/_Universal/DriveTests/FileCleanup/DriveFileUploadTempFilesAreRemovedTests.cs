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
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
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


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanUploadFileWith2PayloadsAnd2ThumbnailsAndTempFilesAreDeleted(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
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

        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

        // 
        // verify payloads are in place
        //
        foreach (var definition in testPayloads)
        {
            //test the headers payload info
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == definition.Key);
            ClassicAssert.IsTrue(definition.Thumbnails.Count == payload.Thumbnails.Count);
            ClassicAssert.IsTrue(definition.ContentType == payload.ContentType);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(definition.Iv, payload.Iv));

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

        // 
        // verify payload temp files are gone
        //
        foreach (var descriptor in header.FileMetadata.Payloads)
        {
            var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
            var payloadTempFileExistsResponse = await ownerApiClient.DriveRedux.TempFileExists(
                uploadResult.File, TempStorageType.Upload, payloadExtension);

            ClassicAssert.IsTrue(payloadTempFileExistsResponse.IsSuccessStatusCode);
            ClassicAssert.IsFalse(payloadTempFileExistsResponse.Content);

            foreach (var thumbnail in descriptor.Thumbnails)
            {
                var thumbnailExtension =
                    DriveFileUtility.GetThumbnailFileExtension(descriptor.Key, descriptor.Uid, thumbnail.PixelWidth, thumbnail.PixelHeight);
                var thumbnailTempFileExistsResponse = await ownerApiClient.DriveRedux.TempFileExists(
                    uploadResult.File, TempStorageType.Upload, thumbnailExtension);

                ClassicAssert.IsTrue(thumbnailTempFileExistsResponse.IsSuccessStatusCode);
                ClassicAssert.IsFalse(thumbnailTempFileExistsResponse.Content);
            }
        }
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanUpdateFilePayloadsAndThumbnailsAndOrphansAreDeleted(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        Console.WriteLine($"Test data path: [{_scaffold.TestPayloadPath}]");
        Console.WriteLine($"Test payload data path: [{_scaffold.TestPayloadPath}]");
        
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

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

        await callerContext.Initialize(ownerApiClient);

        var updateFileResponse =
            await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd, payloadWithModifiedThumbnails]);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // 
        // verify payload temp files are gone
        //

        // get the latest header of the updated file
        var getUpdatedHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        foreach (var descriptor in getUpdatedHeaderResponse.Content!.FileMetadata.Payloads)
        {
            var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
            var payloadTempFileExistsResponse = await ownerApiClient.DriveRedux.TempFileExists(
                uploadResult.File, TempStorageType.Upload, payloadExtension);

            ClassicAssert.IsTrue(payloadTempFileExistsResponse.IsSuccessStatusCode);
            ClassicAssert.IsFalse(payloadTempFileExistsResponse.Content);

            foreach (var thumbnail in descriptor.Thumbnails)
            {
                var thumbnailExtension = DriveFileUtility.GetThumbnailFileExtension(descriptor.Key, 
                    descriptor.Uid, thumbnail.PixelWidth, thumbnail.PixelHeight);
                
                var thumbnailTempFileExistsResponse = await ownerApiClient.DriveRedux.TempFileExists(
                    uploadResult.File, TempStorageType.Upload, thumbnailExtension);

                ClassicAssert.IsTrue(thumbnailTempFileExistsResponse.IsSuccessStatusCode);
                ClassicAssert.IsFalse(thumbnailTempFileExistsResponse.Content);
            }
        }

        // 
        // verify there are no orphans for the deleted payloads and thumbnails
        //
        var hasOrphanPayloadsResponse = await ownerApiClient.DriveRedux.HasOrphanPayloads(targetFile);
        Assert.That(hasOrphanPayloadsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(hasOrphanPayloadsResponse.Content, Is.False);
    }
}