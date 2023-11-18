using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive;

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

    [Test]
    public async Task GetPayloadUsingValidPayloadKeyButPayloadDoesNotExistReturns404()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail1,
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);

        // now that we know we have a valid file with a few payloads
        var getRandomPayload = await client.DriveRedux.GetPayload(uploadResult.File, "r3nd0m09");
        Assert.IsTrue(getRandomPayload.StatusCode == HttpStatusCode.NotFound, $"Status code was {getRandomPayload.StatusCode}");
    }

    [Test]
    public async Task CanModifyPayloadOnExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var uploadNewMetadataResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);

        Assert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;


        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await client.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.IsNotNull(headerBeforeUpload);

        //
        // Now add a payload
        //
        var uploadedPayloadDefinition = TestPayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadPayloadResponse = await client.DriveRedux.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        Assert.IsTrue(uploadPayloadResponse.IsSuccessStatusCode);
        Assert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await client.DriveRedux.GetFileHeader(targetFile);
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
        var getPayloadResponse = await client.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
        var payloadBytes = await getPayloadResponse.Content.ReadAsByteArrayAsync();
        Assert.IsTrue(payloadBytes.Length == thePayloadDescriptor.BytesWritten);
    }

    [Test]
    public async Task CanDeletePayloadOnExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var uploadedPayloadDefinition = TestPayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewFileResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await client.DriveRedux.GetFileHeader(targetFile);
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

        // Delete the payload
        var deletePayloadResponse = await client.DriveRedux.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        Assert.IsTrue(deletePayloadResponse.IsSuccessStatusCode);
        var deletePayloadResult = deletePayloadResponse.Content;
        Assert.IsNotNull(deletePayloadResult);

        Assert.IsTrue(deletePayloadResult.NewVersionTag != targetVersionTag, "version tag should have changed");
        Assert.IsTrue(deletePayloadResult.NewVersionTag != Guid.Empty);

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await client.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.IsNotNull(headerAfterPayloadWasUploaded);

        Assert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == deletePayloadResult.NewVersionTag,
            "Version tag should match the one set by deleting the payload");

        // Payload should not be in header
        Assert.IsFalse(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Any());

        // Payload should return 404
        var getPayloadResponse = await client.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
    }

    [Test]
    public async Task FailToDeletePayloadOnExistingFileWhenInvalidVersionTagIsSpecified()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var uploadedPayloadDefinition = TestPayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewFileResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-128d8b157c80"); // an invalid version tag

        // Validate payload exists on the file

        // Get the latest file header
        var getHeaderBeforeDeletingPayloadResponse = await client.DriveRedux.GetFileHeader(targetFile);
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

        // Attempt Delete the payload
        var deletePayloadResponse = await client.DriveRedux.DeletePayload(targetFile, targetVersionTag, uploadedPayloadDefinition.Key);
        Assert.IsTrue(deletePayloadResponse.StatusCode == HttpStatusCode.BadRequest);
        var deletePayloadResult = deletePayloadResponse.Content;
        Assert.IsNull(deletePayloadResult);

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await client.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.IsNotNull(headerAfterPayloadWasUploaded);

        // Payload should still be in header
        Assert.IsTrue(headerBeforePayloadDeleted.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptorAfterAttemptingDelete =
            headerBeforePayloadDeleted.FileMetadata.Payloads.SingleOrDefault(p => p.Key == uploadedPayloadDefinition.Key);
        Assert.IsNotNull(thePayloadDescriptorAfterAttemptingDelete);
        Assert.IsTrue(thePayloadDescriptorAfterAttemptingDelete.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptorAfterAttemptingDelete.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        Assert.IsTrue(thePayloadDescriptorAfterAttemptingDelete.BytesWritten == uploadedPayloadDefinition.Content.Length);

        // Payload should still be on server
        var getPayloadResponse = await client.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
    }

    [Test]
    public async Task FailWhenModifyingPayloadOnExistingFileAndInvalidVersionTagIsSpecified()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var uploadNewMetadataResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);

        Assert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = Guid.Parse("00000000-0000-0000-0000-928d8b157c80"); // an invalid version tag

        //
        // Now add a payload
        //
        var uploadedPayloadDefinition = TestPayloadDefinitions.PayloadDefinition1;
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadPayloadResponse = await client.DriveRedux.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        Assert.IsTrue(uploadPayloadResponse.StatusCode == HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task FailWhenDuplicatePayloadKeys()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        //Note: the duplicate keys
        var testPayloads = new List<TestPayloadDefinition>()
        {
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail1, //Note: the duplicate keys are intentional
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail1 //Note: the duplicate keys are intentional
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, $"Status code was {response.StatusCode}");
    }

    [Test]
    public async Task FailIfPayloadKeyIncludesInvalidChars()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

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

            var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, $"Invalid Key {invalidKey} should have failed");
        }
    }

    [Test]
    public async Task GetPayloadByKeyIncludesCorrectHeaders()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var payloadDefinition = TestPayloadDefinitions.PayloadDefinitionWithThumbnail1;
        var testPayloads = new List<TestPayloadDefinition>() { payloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 1);

        var payloadFromHeader = header.FileMetadata.Payloads.SingleOrDefault(p => p.Key == payloadDefinition.Key);
        Assert.IsNotNull(payloadFromHeader, "payload not found in header");

        // Get the payload and check the headers
        var getPayloadKey1Response = await client.DriveRedux.GetPayload(uploadResult.File, TestPayloadDefinitions.PayloadDefinitionWithThumbnail1.Key);

        Assert.IsTrue(getPayloadKey1Response.IsSuccessStatusCode);
        Assert.IsNotNull(getPayloadKey1Response.ContentHeaders);
        Assert.IsNotNull(getPayloadKey1Response.Headers);

        Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues));
        Assert.IsFalse(bool.Parse(isEncryptedValues.Single()));

        Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues));
        Assert.IsTrue(payloadKeyValues.Single() == payloadDefinition.Key);
        Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues));
        Assert.IsTrue(contentTypeValues.Single() == payloadDefinition.ContentType);

        Assert.IsTrue(getPayloadKey1Response.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedHeader64, out var encryptedHeader64Values));
        Assert.IsTrue(encryptedHeader64Values.Single() == header.SharedSecretEncryptedKeyHeader.ToBase64());
       
        Assert.IsTrue(DriveFileUtility.TryParseLastModifiedHeader(getPayloadKey1Response.ContentHeaders, out var lastModifiedHeaderValue));
        //Note commented as I'm having some conversion issues i think
        Assert.IsTrue(lastModifiedHeaderValue.GetValueOrDefault().seconds == payloadFromHeader.LastModified.seconds);
    }

}