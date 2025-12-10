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
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
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
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Samwise });
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

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(uploadedPayloadDefinition.Key);
        ClassicAssert.IsNotNull(payloadFromHeader, "payload not found in header");
        ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader.Iv, uploadedPayloadDefinition.Iv));

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        // Get the payload and check the headers
        var getPayloadKey1Response = await uniDriveClient.GetPayload(uploadResult.File, uploadedPayloadDefinition.Key);

        ClassicAssert.IsTrue(getPayloadKey1Response.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsNotNull(getPayloadKey1Response.ContentHeaders);
            ClassicAssert.IsNotNull(getPayloadKey1Response.Headers);

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
            ClassicAssert.IsFalse(bool.Parse(isEncryptedValues.Single()));

            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
            ClassicAssert.IsTrue(payloadKeyValues.Single() == uploadedPayloadDefinition.Key);
            ClassicAssert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
            ClassicAssert.IsTrue(contentTypeValues.Single() == uploadedPayloadDefinition.ContentType);

            ClassicAssert.IsFalse(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out _));

            ClassicAssert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders, out var lastModifiedHeaderValue));
            //Note commented as I'm having some conversion issues i think
            ClassicAssert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
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

        ClassicAssert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforeUpload);

        //
        // Now add a payload
        //
        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
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
        ClassicAssert.IsTrue(uploadPayloadResponse.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");

            // Get the latest file header
            var getHeaderAfterPayloadUploadedResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
            var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
            ClassicAssert.IsNotNull(headerAfterPayloadWasUploaded);

            ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == uploadPayloadResponse.Content.NewVersionTag,
                "Version tag should match the one set by uploading the new payload");

            // Payload should be listed 
            ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count() == 1);
            var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads
                .SingleOrDefault(p => p.KeyEquals( uploadedPayloadDefinition.Key));
            ClassicAssert.IsNotNull(thePayloadDescriptor);
            ClassicAssert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
            CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
            ClassicAssert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

            // Last modified should be changed
            ClassicAssert.IsTrue(thePayloadDescriptor.LastModified > headerBeforeUpload.FileMetadata.Updated);

            // Get the payload
            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
            ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            var payloadBytes = await getPayloadResponse.Content.ReadAsByteArrayAsync();
            ClassicAssert.IsTrue(payloadBytes.Length == thePayloadDescriptor.BytesWritten);
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

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewFileResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeDeletingPayloadResponse.IsSuccessStatusCode);
        var headerBeforePayloadDeleted = getHeaderBeforeDeletingPayloadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforePayloadDeleted);

        // Payload should be listed 
        ClassicAssert.IsTrue(headerBeforePayloadDeleted.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptor = headerBeforePayloadDeleted.FileMetadata.Payloads
            .SingleOrDefault(p => p.KeyEquals( uploadedPayloadDefinition.Key));
        ClassicAssert.IsNotNull(thePayloadDescriptor);
        ClassicAssert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        ClassicAssert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        // Delete the payload
        var deletePayloadResponse = await uniDriveClient.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        ClassicAssert.IsTrue(deletePayloadResponse.StatusCode == expectedStatusCode);

        // Test More
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var deletePayloadResult = deletePayloadResponse.Content;
            ClassicAssert.IsNotNull(deletePayloadResult);

            ClassicAssert.IsTrue(deletePayloadResult.NewVersionTag != targetVersionTag, "version tag should have changed");
            ClassicAssert.IsTrue(deletePayloadResult.NewVersionTag != Guid.Empty);

            // Get the latest file header
            var getHeaderAfterPayloadUploadedResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
            var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
            ClassicAssert.IsNotNull(headerAfterPayloadWasUploaded);

            ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == deletePayloadResult.NewVersionTag,
                "Version tag should match the one set by deleting the payload");

            // Payload should not be in header
            ClassicAssert.IsFalse(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Any());

            // Payload should return 404
            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
            ClassicAssert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
        }
    }
}