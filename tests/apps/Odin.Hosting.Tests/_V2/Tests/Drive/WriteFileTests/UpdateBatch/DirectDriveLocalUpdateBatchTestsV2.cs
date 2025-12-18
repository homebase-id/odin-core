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
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteFileTests.UpdateBatch;

public class DirectDriveLocalUpdateBatchTestsV2
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
        // yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };

        // yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };

        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }


    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUpdateBatchWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;

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
            File = targetFile.ToV2FileIdentifier(),
            
            
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUpdateBatchWith1PayloadsAnd1Thumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillBeDeleted.ToPayloadDescriptor()]
        };

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive,
            uploadedFileMetadata, uploadManifest, [payloadThatWillBeDeleted]);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var targetFile = uploadNewFileResponse.Content!.File;
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition2();

        // create instruction set
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToV2FileIdentifier(),
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

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payloadThatWillBeDeleted.Key),
                "payload 1 should have been removed");
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");


            //
            // Ensure payloadToAdd add is added
            //
            var getPayloadToAddResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadToAdd.Key);
            ClassicAssert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(
                getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, payloadToAdd.Content);

            // Check all the thumbnails
            foreach (var thumbnail in payloadToAdd.Thumbnails)
            {
                var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(targetFile, thumbnail.PixelWidth,
                    thumbnail.PixelHeight, payloadToAdd.Key);

                ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }

            //
            // Ensure we get 404 for the payload1
            //
            var getPayload1Response = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadThatWillBeDeleted.Key);
            ClassicAssert.IsTrue(getPayload1Response.StatusCode == HttpStatusCode.NotFound);

            //
            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUpdateBatchWith1PayloadsAnd1ThumbnailsHandleOrphanThumbnails(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive,
            uploadedFileMetadata, uploadManifest, [payloadThatWillLoseAThumbnail]);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

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
            File = targetFile.ToV2FileIdentifier(),
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

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata,
        [
            payloadToAdd,
            payloadThatWillLoseAThumbnail
        ]);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadThatWillLoseAThumbnail.Key));
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");


            //
            // Ensure payloadToAdd add is added
            //
            var getPayloadToAddResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadToAdd.Key);
            ClassicAssert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(
                getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, payloadToAdd.Content);

            // Check all the thumbnails
            foreach (var thumbnail in payloadToAdd.Thumbnails)
            {
                var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(targetFile, thumbnail.PixelWidth,
                    thumbnail.PixelHeight, payloadToAdd.Key);

                ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }

            //
            // payloadThatWillLoseAThumbnail should still be on the server but not have the thumbnailToBeDeleted  
            //
            var getPayloadThatWillLoseAThumbnailResponse =
                await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadThatWillLoseAThumbnail.Key);
            ClassicAssert.IsTrue(getPayloadThatWillLoseAThumbnailResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadThatWillLoseAThumbnailResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(getPayloadThatWillLoseAThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                 DateTimeOffset.Now.AddSeconds(10));

            var payloadThatWillLoseAThumbnailContent =
                (await getPayloadThatWillLoseAThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(payloadThatWillLoseAThumbnailContent, payloadThatWillLoseAThumbnail.Content);

            // Check all the thumbnails
            foreach (var thumbnail in payloadThatWillLoseAThumbnail.Thumbnails)
            {
                var getThumbnailResponseForPayloadThatWillLoseAThumbnail = await ownerApiClient.DriveRedux.GetThumbnail(targetFile,
                    thumbnail.PixelWidth,
                    thumbnail.PixelHeight, payloadThatWillLoseAThumbnail.Key);

                ClassicAssert.IsTrue(getThumbnailResponseForPayloadThatWillLoseAThumbnail.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getThumbnailResponseForPayloadThatWillLoseAThumbnail.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getThumbnailResponseForPayloadThatWillLoseAThumbnail.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponseForPayloadThatWillLoseAThumbnail.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }

            //
            // Get a 404 for the thumbnailToBeDeleted
            //
            var getThumbnailToBeDeletedResponse = await ownerApiClient.DriveRedux.GetThumbnail(targetFile, thumbnailToBeDeleted.PixelWidth,
                thumbnailToBeDeleted.PixelHeight, payloadThatWillLoseAThumbnail.Key, directMatchOnly: true);
            ClassicAssert.IsTrue(getThumbnailToBeDeletedResponse.StatusCode == HttpStatusCode.NotFound);

            //
            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }
    
    
    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUpdateBatchByIdentifyingFileWithUniqueIdWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.UniqueId = Guid.NewGuid();
        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;

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
                DriveId = callerContext.DriveId
            },
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }
    
    
    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task CanUpdateBatchByIdentifyingFileWithGlobalTransitIdWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.UniqueId = Guid.NewGuid();
        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;

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
                DriveId = callerContext.DriveId
            },
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }
}