using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Redux.DriveApi.DirectDrive;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayloadTests_3
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
    public async Task CanUpload_Encrypted_PayloadOnExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        Assert.Inconclusive("need to encrypt");
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

        var uploadNewMetadataResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);

        Assert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        var targetFile = uploadResult.File;
        var targetVersionTag = uploadResult.NewVersionTag;

        // Get the header before we make changes so we have a baseline
        var getHeaderBeforeUploadResponse = await client.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.IsNotNull(headerBeforeUpload);

        // Now add a payload

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
    public Task DeletingPayloadAlsoDeletesRecipientsPayloads()
    {
        Assert.Inconclusive("TODO - decide on deleting payloads");
        return Task.CompletedTask;
    }
}