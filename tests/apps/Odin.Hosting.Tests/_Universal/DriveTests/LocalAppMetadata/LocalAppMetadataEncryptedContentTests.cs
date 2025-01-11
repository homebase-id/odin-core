using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.DriveTests.LocalAppMetadata;

public class LocalAppMetadataEncryptedContentTests
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

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }
    
    public async Task CanUpdateLocalAppMetadataContent(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        
        Assert.Inconclusive("TODO: support encryption");
        
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        // Act
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var request = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            Content = "some local content here"
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        var result = response.Content;
        Assert.IsFalse(result.NewLocalVersionTag == Guid.Empty);

        // Assert - getting the file should include the metadata
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        var theUpdatedFile = updatedFileResponse.Content;
        // Assert.IsTrue(theUpdatedFile.);
    }

 
    // TODO
}