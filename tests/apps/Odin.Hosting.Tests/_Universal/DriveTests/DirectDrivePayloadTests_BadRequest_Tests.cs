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
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayloadTests_BadRequest_Tests
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


    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.BadRequest };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.BadRequest };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.BadRequest };
    }
    
    public static IEnumerable OwnerAndAppBadRequest()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.BadRequest };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.BadRequest };
    }
    
    public static IEnumerable GuestForbidden()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAndAppBadRequest))]
    [TestCaseSource(nameof(GuestForbidden))]
    public async Task FailToDeletePayloadOnExistingFileWhenInvalidVersionTagIsSpecified(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-128d8b157c80"); // an invalid version tag

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeDeletingPayloadResponse.IsSuccessStatusCode);
        var headerBeforePayloadDeleted = getHeaderBeforeDeletingPayloadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforePayloadDeleted);

        // Payload should be listed 
        ClassicAssert.IsTrue(headerBeforePayloadDeleted.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptor = headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.Key == uploadedPayloadDefinition.Key);
        ClassicAssert.IsNotNull(thePayloadDescriptor);
        ClassicAssert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        ClassicAssert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

        // Attempt Delete the payload
        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var deletePayloadResponse = await uniDriveClient.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        ClassicAssert.IsTrue(deletePayloadResponse.StatusCode == expectedStatusCode, $"Actual status code: {deletePayloadResponse.StatusCode}");
        var deletePayloadResult = deletePayloadResponse.Content;
        ClassicAssert.IsNull(deletePayloadResult);

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        ClassicAssert.IsNotNull(headerAfterPayloadWasUploaded);

        // Payload should still be in header
        ClassicAssert.IsTrue(headerBeforePayloadDeleted.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptorAfterAttemptingDelete =
            headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.Key == uploadedPayloadDefinition.Key);
        ClassicAssert.IsNotNull(thePayloadDescriptorAfterAttemptingDelete);
        ClassicAssert.IsTrue(thePayloadDescriptorAfterAttemptingDelete.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptorAfterAttemptingDelete.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        ClassicAssert.IsTrue(thePayloadDescriptorAfterAttemptingDelete.BytesWritten == uploadedPayloadDefinition.Content.Length);

        // Payload should still be on server
        var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAndAppBadRequest))]
    [TestCaseSource(nameof(GuestForbidden))]
    public async Task FailWhenModifyingPayloadOnExistingFileAndInvalidVersionTagIsSpecified(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        ClassicAssert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-928d8b157c80"); // an invalid version tag

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
        ClassicAssert.IsTrue(uploadPayloadResponse.StatusCode == expectedStatusCode, $"Actual status code: {uploadPayloadResponse.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailWhenDuplicatePayloadKeys(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        //Note: the duplicate keys
        var testPayloads = new List<TestPayloadDefinition>()
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(), //Note: the duplicate keys are intentional
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1() //Note: the duplicate keys are intentional
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        await callerContext.Initialize(ownerApiClient);
        var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var response = await uniDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Status code was {response.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailIfPayloadKeyIncludesInvalidChars(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        const string pkTooLong = "abckjalcialakk";
        const string pkNoCapitalLettersAllowed = "ABC23duu";

        var invalidKeys = new List<string>()
        {
            pkTooLong, pkNoCapitalLettersAllowed
        };

        foreach (var invalidKey in invalidKeys)
        {
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Key = invalidKey,
                    ContentType = "text/plain",
                    Content = "some content for payload key 1".ToUtf8ByteArray(),
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            await callerContext.Initialize(ownerApiClient);
            var uniDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

            var response = await uniDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Status code was {response.StatusCode}.  Invalid Key {invalidKey} should have failed");
        }
    }
}