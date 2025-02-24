using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public class DirectDriveGeneralFileTests_Encrypted
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


    public static IEnumerable AppReadWriteAllowed()
    {
        yield return new object[] { new AppReadWriteAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppWriteOnlyForbidden()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    public static IEnumerable AppWriteOnlyAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestWriteOnlyAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestWriteOnlyForbidden()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    public static IEnumerable GuestReadOnlyForbidden()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(GuestWriteOnlyForbidden))]
    [TestCaseSource(nameof(GuestReadOnlyForbidden))]
    [TestCaseSource(nameof(AppWriteOnlyForbidden))]
    [TestCaseSource(nameof(AppReadWriteAllowed))]
    public async Task CanUpdateEncryptedMetadataWithStorageIntent_MetadataOnly(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        const string originalContent = "some content here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;
        await callerContext.Initialize(ownerApiClient);

        // Act
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var originalKeyHeader = KeyHeader.NewRandom16();
        var (response, originalEncryptedJsonContent64) =
            await callerDriveClient.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, originalKeyHeader);

        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = response.Content;
            var getHeaderResponse1 = await callerDriveClient.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
            var uploadedFile1 = getHeaderResponse1.Content;

            const string updatedContent = "updated information and content here";
            var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
            updatedMetadata.AppData.Content = updatedContent;
            updatedMetadata.VersionTag = uploadedFile1.FileMetadata.VersionTag;
            updatedMetadata.IsEncrypted = true;

            var newKeyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
            };

            var (updateResponse, updatedEncryptedJsonContent64) =
                await callerDriveClient.UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);

            ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(originalEncryptedJsonContent64 != updatedEncryptedJsonContent64,
                "original encrypted content should not match updated encrypted content since the IV changed");

            // grab the file again
            var getHeaderResponse2 = await callerDriveClient.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(getHeaderResponse2.IsSuccessStatusCode);

            var theUpdatedFile = getHeaderResponse2.Content;
            var updatedContentDecryptedWithOriginalKeyHeader =
                originalKeyHeader.Decrypt(Convert.FromBase64String(theUpdatedFile.FileMetadata.AppData.Content));
            ClassicAssert.IsTrue(updatedContentDecryptedWithOriginalKeyHeader.ToStringFromUtf8Bytes() != updatedContent);

            var updatedContentDecryptedWithNewKeyHeader = newKeyHeader.Decrypt(Convert.FromBase64String(theUpdatedFile.FileMetadata.AppData.Content));
            ClassicAssert.IsTrue(updatedContentDecryptedWithNewKeyHeader.ToStringFromUtf8Bytes() == updatedContent);
        }
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppReadWriteAllowed))]
    [TestCaseSource(nameof(AppWriteOnlyForbidden))]
    [TestCaseSource(nameof(GuestWriteOnlyForbidden))]
    [TestCaseSource(nameof(GuestReadOnlyForbidden))]
    public async Task CanUpdateEncryptedMetadata_That_Has_A_Payload_WithStorageIntent_MetadataOnly(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        const string originalContent = "original content is here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        var p = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        p.Iv = ByteArrayUtil.GetRndByteArray(16);
        List<TestPayloadDefinition> testPayloads = [p];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        await callerContext.Initialize(ownerApiClient);

        var originalKeyHeader = KeyHeader.NewRandom16();

        // upload a file with payloads
        var (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedFile(targetDrive, originalKeyHeader, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // Get the file from the server
        var getOriginalHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getOriginalHeaderResponse.IsSuccessStatusCode);
        var uploadedFile1 = getOriginalHeaderResponse.Content;

        //
        // Now, Change just header
        //
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        const string updatedContent = "updated information and content here";
        var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updatedMetadata.AppData.Content = updatedContent;
        updatedMetadata.VersionTag = uploadedFile1.FileMetadata.VersionTag;
        updatedMetadata.IsEncrypted = true;

        var newKeyHeader = new KeyHeader()
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var (updateResponse, updatedEncryptedJsonContent64) =
            await callerDriveClient.UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);
        ClassicAssert.IsTrue(updateResponse.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {updateResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            // then validate that I can decrypt the payloads

            var getUpdatedHeaderResponse = await callerDriveClient.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(getUpdatedHeaderResponse.IsSuccessStatusCode);
            var updatedHeaderResponse = getUpdatedHeaderResponse.Content;
            ClassicAssert.IsNotNull(updatedHeaderResponse);
            ClassicAssert.IsTrue(updatedHeaderResponse.FileMetadata.AppData.Content != uploadedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(updatedHeaderResponse.FileMetadata.Payloads.Count() == testPayloads.Count);

            // Get the payloads
            var definition = testPayloads.First();
            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));

            //
            // Validate that I can still decrypt using the original AES key
            //
            var payloadDescriptor = updatedHeaderResponse.FileMetadata.Payloads.Single(p => p.Key == payloadKeyValues.First());
            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadDescriptor.Iv,
                AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
            };

            var encryptedPayloadContent = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            var decryptedPayloadContent = payloadKeyHeader.Decrypt(encryptedPayloadContent);
            ClassicAssert.IsTrue(decryptedPayloadContent.ToStringFromUtf8Bytes() == definition.Content.ToStringFromUtf8Bytes());

            // Check all the thumbnails
            var thumbnail = definition.Thumbnails.First();

            var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File,
                thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
            ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);


            var encryptedThumbnailContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
            var decryptedThumbnailContent = payloadKeyHeader.Decrypt(encryptedThumbnailContent);
            ClassicAssert.IsTrue(decryptedThumbnailContent.ToStringFromUtf8Bytes() == thumbnail.Content.ToStringFromUtf8Bytes());
        }
    }
}