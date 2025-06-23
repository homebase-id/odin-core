using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
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
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;
        uploadedFileMetadata.AppData.Content = "some new content here...";

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, keyHeader);
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
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) = await callerDriveClient.UpdateEncryptedFile(
            updateInstructionSet,
            updatedFileMetadata,
            [], keyHeader);

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
            ClassicAssert.IsTrue(header.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
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
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payloadThatWillBeDeleted.Iv = ByteArrayUtil.GetRndByteArray(16);
        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillBeDeleted.ToPayloadDescriptor()]
        };


        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _, _, _) = await ownerApiClient.DriveRedux.UploadNewEncryptedFile(targetDrive, keyHeader,
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

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, encryptedPayloads, encryptedThumbnails) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);

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
            ClassicAssert.IsTrue(header.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
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
            ClassicAssert.IsTrue(content.ToBase64() == encryptedPayloads.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64);


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
                ClassicAssert.IsTrue(thumbContent.ToBase64() ==
                                     encryptedThumbnails.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64);
            }

            //
            // Ensure we get 404 for the payload1
            //
            var getPayload1Response = await ownerApiClient.DriveRedux.GetPayload(targetFile, payloadThatWillBeDeleted.Key);
            ClassicAssert.IsTrue(getPayload1Response.StatusCode == HttpStatusCode.NotFound);

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
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