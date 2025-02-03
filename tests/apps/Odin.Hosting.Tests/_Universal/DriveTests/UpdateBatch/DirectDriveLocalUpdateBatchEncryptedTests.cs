using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.DriveTests.UpdateBatch;

public class DirectDriveLocalUpdateBatchEncryptedTests
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
        yield return new object[] { new AppReadWriteAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    public static IEnumerable WhenGuestOnlyHasReadAccess()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchEncryptedWithoutPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some new content here...";

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, keyHeader);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

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
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) = await callerDriveClient.UpdateEncryptedFile(
            updateInstructionSet,
            updatedFileMetadata,
            []);

        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.IsEncrypted);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsFalse(header.FileMetadata.Payloads.Any());

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            Assert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileSearchResult);
            Assert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchEncryptedWith1PayloadsAnd1Thumbnails(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads 
        // 
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some new content here...";
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payloadThatWillBeDeleted.Iv = ByteArrayUtil.GetRndByteArray(16);
        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillBeDeleted.ToPayloadDescriptor()]
        };


        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _, _, _) = await ownerApiClient.DriveRedux.UploadNewEncryptedFile(targetDrive, keyHeader,
            uploadedFileMetadata, uploadManifest, [payloadThatWillBeDeleted]);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

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
        payloadToAdd.Iv = ByteArrayUtil.GetRndByteArray(16);

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
                    payloadToAdd.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite),
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payloadThatWillBeDeleted.Key
                    }
                ]
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, encryptedPayloads, encryptedThumbnails) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Get the updated file and test it
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.IsEncrypted);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.Payloads.Count() == 1);
            Assert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payloadThatWillBeDeleted.Key),
                "payload 1 should have been removed");
            Assert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");


            //
            // Ensure payloadToAdd add is added
            //
            var getPayloadToAddResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadToAdd.Key);
            Assert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
            Assert.IsTrue(content.ToBase64() == encryptedPayloads.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64);

            
            // Check all the thumbnails
            foreach (var thumbnail in payloadToAdd.Thumbnails)
            {
                var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(targetFile, thumbnail.PixelWidth,
                    thumbnail.PixelHeight, payloadToAdd.Key);

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                Assert.IsTrue(thumbContent.ToBase64() == encryptedThumbnails.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64);

            }

            //
            // Ensure we get 404 for the payload1
            //
            var getPayload1Response = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadThatWillBeDeleted.Key);
            Assert.IsTrue(getPayload1Response.StatusCode == HttpStatusCode.NotFound);

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            Assert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileSearchResult);
            Assert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);
        }
    }
}