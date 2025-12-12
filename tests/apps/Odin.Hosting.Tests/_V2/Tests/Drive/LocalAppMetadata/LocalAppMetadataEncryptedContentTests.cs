using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._V2.Tests.Drive.LocalAppMetadata;

public class LocalAppMetadataEncryptedContentTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin });
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
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task CanUpdateLocalAppMetadataContentForEncryptedTargetFile(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "data data data";
       
        var keyHeader = KeyHeader.NewRandom16();

        // Act
        var (prepareFileResponse, _) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, keyHeader);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        var content = "some local content here";
        var encryptedLocalMetadataContent = AesCbc.Encrypt(content.ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var request = new UpdateLocalMetadataContentRequest()
        {
            Iv = localContentIv,
            File = targetFile,
            Content = encryptedLocalMetadataContent.ToBase64()
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(targetFile.FileId, targetFile.TargetDrive.Alias,request);
        var result = response.Content;
        ClassicAssert.IsFalse(result.NewLocalVersionTag == Guid.Empty);

        // Assert - getting the file should include the metadata
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        var theUpdatedFile = updatedFileResponse.Content;

        var decryptedBytes = AesCbc.Decrypt(theUpdatedFile.FileMetadata.LocalAppData.Content.FromBase64(), keyHeader.AesKey, request.Iv);
        ClassicAssert.IsTrue(decryptedBytes.ToStringFromUtf8Bytes() == content);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task FailsWithBadRequestWhenMissingIvOnEncryptedTargetFile(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "data data data";
       
        var keyHeader = KeyHeader.NewRandom16();

        // Act
        var (prepareFileResponse, _) =
            await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, keyHeader);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        var content = "some local content here";
        var encryptedLocalMetadataContent = AesCbc.Encrypt(content.ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var request = new UpdateLocalMetadataContentRequest()
        {
            Iv = Guid.Empty.ToByteArray(), //weak or no key
            File = targetFile,
            Content = encryptedLocalMetadataContent.ToBase64()
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(targetFile.FileId, targetFile.TargetDrive.Alias,request);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
    }
}