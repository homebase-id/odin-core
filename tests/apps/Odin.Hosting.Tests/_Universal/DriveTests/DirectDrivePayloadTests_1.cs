using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer.Encryption;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayloadTests_1
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
    public async Task CanGetPayloadByKeyIncludesCorrectHeaders(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.PayloadDefinitionWithThumbnail1;
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(uploadedPayloadDefinition.Key);
        Assert.IsNotNull(payloadFromHeader, "payload not found in header");
        Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, uploadedPayloadDefinition.Iv));

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        // Get the payload and check the headers
        var getPayloadKey1Response = await uniDriveClient.GetPayload(uploadResult.File, SamplePayloadDefinitions.PayloadDefinitionWithThumbnail1.Key);

        Assert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            Assert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            Assert.IsNotNull(getPayloadKey1Response.Headers);

            Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            Assert.IsFalse(bool.Parse(isEncryptedValues.Single()));

            Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            Assert.IsTrue(payloadKeyValues.Single() == uploadedPayloadDefinition.Key);
            Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            Assert.IsTrue(contentTypeValues.Single() == uploadedPayloadDefinition.ContentType);

            Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var encryptedHeader64Values));

            var payloadEkh = EncryptedKeyHeader.FromBase64(encryptedHeader64Values.Single());
            Assert.IsNotNull(payloadEkh);
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.Iv, uploadedPayloadDefinition.Iv));
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.EncryptedAesKey, header.SharedSecretEncryptedKeyHeader.EncryptedAesKey));

            Assert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders, out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            Assert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanModifyPayloadOnExistingFileAndMetadataIsAutomaticallyUpdated(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        Assert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.IsNotNull(headerBeforeUpload);

        //
        // Now add a payload
        //
        var uploadedPayloadDefinition = SamplePayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var uploadPayloadResponse = await uniDriveClient.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        Assert.IsTrue(uploadPayloadResponse.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            Assert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");

            // Get the latest file header
            var getHeaderAfterPayloadUploadedResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
            var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
            Assert.IsNotNull(headerAfterPayloadWasUploaded);

            Assert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == uploadPayloadResponse.Content.NewVersionTag,
                "Version tag should match the one set by uploading the new payload");

            // Payload should be listed 
            Assert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count() == 1);
            var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads.SingleOrDefault(p => p.Key == uploadedPayloadDefinition.Key);
            Assert.IsNotNull(thePayloadDescriptor);
            Assert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
            CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
            Assert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

            // Last modified should be changed
            Assert.IsTrue(thePayloadDescriptor.LastModified > headerBeforeUpload.FileMetadata.Updated);

            // Get the payload
            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            var payloadBytes = await getPayloadResponse.Content.ReadAsByteArrayAsync();
            Assert.IsTrue(payloadBytes.Length == thePayloadDescriptor.BytesWritten);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanDeletePayloadOnExistingFileAndMetadataIsAutomaticallyUpdated(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewFileResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderBeforeDeletingPayloadResponse.IsSuccessStatusCode);
        var headerBeforePayloadDeleted = getHeaderBeforeDeletingPayloadResponse.Content;
        Assert.IsNotNull(headerBeforePayloadDeleted);

        // Payload should be listed 
        Assert.IsTrue(headerBeforePayloadDeleted.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptor = headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.Key == uploadedPayloadDefinition.Key);
        Assert.IsNotNull(thePayloadDescriptor);
        Assert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        Assert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        // Delete the payload
        var deletePayloadResponse = await uniDriveClient.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        Assert.IsTrue(deletePayloadResponse.StatusCode == expectedStatusCode);

        // Test More
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deletePayloadResult = deletePayloadResponse.Content;
            Assert.IsNotNull(deletePayloadResult);

            Assert.IsTrue(deletePayloadResult.NewVersionTag != targetVersionTag, "version tag should have changed");
            Assert.IsTrue(deletePayloadResult.NewVersionTag != Guid.Empty);

            // Get the latest file header
            var getHeaderAfterPayloadUploadedResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
            var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
            Assert.IsNotNull(headerAfterPayloadWasUploaded);

            Assert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == deletePayloadResult.NewVersionTag,
                "Version tag should match the one set by deleting the payload");

            // Payload should not be in header
            Assert.IsFalse(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Any());

            // Payload should return 404
            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
            Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
        }
    }
}