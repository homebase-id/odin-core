using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.RemotePayload;

public class RemotePayloadTests
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
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanUploadNewFileWithRemotePayloadIdentityAndDescriptors(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var remotePayloadInfo = new RemotePayloadInfo()
        {
            Identity = TestIdentities.Frodo.OdinId,
            DriveId = targetDrive.Alias,
        };
        
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.RemotePayloadInfo = remotePayloadInfo;

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        // Note we add descriptors but no payload binary data
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, payloads: []);

        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            // validate the file is upload and there is a remote payload identity
            var uploadResult = response.Content;
            Assert.That(uploadResult, Is.Not.Null);

            // get the file header
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);

            var header = getHeaderResponse.Content;
            Assert.That(header, Is.Not.Null);
            Assert.That(header.FileMetadata.Payloads.Count() == 1, Is.True);
            Assert.That(header.FileMetadata.RemotePayloadInfo.Identity, Is.EqualTo(remotePayloadInfo.Identity));
            Assert.That(header.FileMetadata.RemotePayloadInfo.DriveId, Is.EqualTo(remotePayloadInfo.DriveId));

            var payloadDescriptor = header.FileMetadata.GetPayloadDescriptor(uploadedPayloadDefinition.Key);
            Assert.That(payloadDescriptor, Is.Not.Null);
            Assert.That(payloadDescriptor.Key, Is.EqualTo(uploadedPayloadDefinition.Key));
            Assert.That(payloadDescriptor.Iv, Is.EquivalentTo(Guid.Empty.ToByteArray()));
            Assert.That(payloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
            Assert.That(payloadDescriptor.DescriptorContent, Is.EqualTo(uploadedPayloadDefinition.DescriptorContent));
            Assert.That(payloadDescriptor.Uid.uniqueTime, Is.EqualTo(0));
            Assert.That(payloadDescriptor.BytesWritten, Is.EqualTo(0));
            Assert.That(payloadDescriptor.PreviewThumbnail.BytesWritten, Is.EqualTo(0));
            Assert.That(payloadDescriptor.PreviewThumbnail.PixelHeight, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.PixelHeight));
            Assert.That(payloadDescriptor.PreviewThumbnail.PixelWidth, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.PixelWidth));
            Assert.That(payloadDescriptor.PreviewThumbnail.ContentType, Is.EqualTo(uploadedPayloadDefinition.PreviewThumbnail.ContentType));
            Assert.That(payloadDescriptor.PreviewThumbnail.Content, Is.EquivalentTo(uploadedPayloadDefinition.PreviewThumbnail.Content));

            foreach (var t in payloadDescriptor.Thumbnails)
            {
                var serverThumbnail = uploadedPayloadDefinition.Thumbnails
                    .SingleOrDefault(x => x.PixelHeight == t.PixelHeight && x.PixelWidth == t.PixelWidth);

                Assert.That(serverThumbnail, Is.Not.Null);
                Assert.That(t.BytesWritten, Is.EqualTo(0));
                Assert.That(t.PixelHeight, Is.EqualTo(serverThumbnail.PixelHeight));
                Assert.That(t.PixelWidth, Is.EqualTo(serverThumbnail.PixelWidth));
                Assert.That(t.ContentType, Is.EqualTo(serverThumbnail.ContentType));
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailToUploadNewFileWhenRemotePayloadIdentityIsSetAndPayloadBinaryIsSentWithUpload(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.RemotePayloadInfo = new RemotePayloadInfo()
        {
            Identity = TestIdentities.Frodo.OdinId,
            DriveId = targetDrive.Alias,
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        Assert.That(response.StatusCode == HttpStatusCode.BadRequest, Is.True);

        var correctErrorCode = _scaffold.GetErrorCode(response.Error) == OdinClientErrorCode.InvalidPayloadContent;
        Assert.That(correctErrorCode, Is.True);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailToUploadNewFileWhenRemotePayloadIdentityIsSetAndNoDescriptorsAreSpecified(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.RemotePayloadInfo = new RemotePayloadInfo()
        {
            Identity = TestIdentities.Frodo.OdinId,
            DriveId = targetDrive.Alias,
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = []
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, []);
        Assert.That(response.StatusCode == HttpStatusCode.BadRequest, Is.True);

        var correctErrorCode = _scaffold.GetErrorCode(response.Error) == OdinClientErrorCode.MissingPayloadKeys;
        Assert.That(correctErrorCode, Is.True);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_UpdateBatch(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var remoteOdinId = new RemotePayloadInfo()
        {
            Identity = TestIdentities.Frodo.OdinId,
            DriveId = targetDrive.Alias
        };
        
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.RemotePayloadInfo = null;

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, payloads: []);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        var uploadedFile = uploadNewFileResponse.Content;
        Assert.That(uploadedFile, Is.Not.Null);

        //
        // Now try to modify the remote identity
        //

        uploadedFileMetadata.RemotePayloadInfo = remoteOdinId;
        uploadedFileMetadata.VersionTag = uploadedFile.NewVersionTag;

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = uploadedFile.File.ToFileIdentifier(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = null
            }
        };

        var response = await callerDriveClient.UpdateFile(updateInstructionSet, uploadedFileMetadata, payloads: []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var errorCode = _scaffold.GetErrorCode(response.Error);
        Assert.That(errorCode, Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_OverwriteMetadata(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var remoteOdinId = new RemotePayloadInfo()
        {
            Identity = TestIdentities.Frodo.OdinId,
            DriveId = targetDrive.Alias,
        };

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition>() { uploadedPayloadDefinition };

        uploadedFileMetadata.RemotePayloadInfo = null;

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, payloads: []);
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadedFile = uploadNewFileResponse.Content;
        Assert.That(uploadedFile, Is.Not.Null);

        //
        // Now try to modify the remote identity
        //

        uploadedFileMetadata.RemotePayloadInfo = remoteOdinId;

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var response = await callerDriveClient.UpdateExistingMetadata(uploadedFile.File, uploadedFile.NewVersionTag, uploadedFileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var errorCode = _scaffold.GetErrorCode(response.Error);
        Assert.That(errorCode, Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
    }


    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task FailToModifyRemotePayloadIdentityOnExistingFile_OverwriteFile(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        await Task.CompletedTask;
        Assert.Pass("Overwrite file is going away");
    }
}