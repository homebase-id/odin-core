using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteFileTests;

public class DirectDrivePayloadTestsV2
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
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK };
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };
        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
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
        uploadedFileMetadata.AccessControlList = AccessControlList.Anonymous;
        
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
        var client = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        // Delete the payload
        var deletePayloadResponse = await client.DeletePayload(callerContext.DriveId, targetFile.FileId, uploadedPayloadDefinition.Key, 
            targetVersionTag);
        ClassicAssert.IsTrue(deletePayloadResponse.StatusCode == expectedStatusCode, $"code was {deletePayloadResponse.StatusCode}");

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