using System;
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;

namespace Odin.Hosting.Tests._Universal.DriveTests.LocalAppMetadata;

public class LocalAppMetadataContentTests
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

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestWriteOnlyAccessToDrive()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestWriteOnlyAccessToDrive))]
    public async Task CanUpdateLocalAppMetadataContentWhenNotSetInTargetFile(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;
        
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        const string content = "some local content here";
        var request = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = content
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var result = response.Content;
            ClassicAssert.IsFalse(result.NewLocalVersionTag == Guid.Empty);

            // Get the file and see that it's updated
            var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
            var theUpdatedFile = updatedFileResponse.Content;
            ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
            ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.Content == content);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task CanUpdateLocalAppMetadataContentWhenSetInTargetFileUsingValidLocalVersionTag(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        const string content1 = "some local content here";
        const string content2 = "other info here";

        var request1 = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = content1
        };

        // first update - just use the owner api so we can prepare a file with a nonempty local version tag
        var prepareLocalMetadataResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataContent(request1);
        ClassicAssert.IsTrue(prepareLocalMetadataResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {prepareLocalMetadataResponse.StatusCode}");

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(updatedFileResponse1.IsSuccessStatusCode);
        var latestLocalVersionTag = updatedFileResponse1.Content.FileMetadata.LocalAppData.VersionTag;

        //
        // Act - try to update the local metadata with a bad local version tag
        //
        var request2 = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = latestLocalVersionTag,
            Content = content2
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.Content == content2, "the content should have changed");
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task TagsAreNotChangedWhenUpdatingLocalMetadataContent(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;


        // Set some tags

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var prepareTagsRequest = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var prepareTagsResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataTags(prepareTagsRequest);
        ClassicAssert.IsTrue(prepareTagsResponse.IsSuccessStatusCode);
        
        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(updatedFileResponse1.IsSuccessStatusCode);
        CollectionAssert.AreEquivalent(updatedFileResponse1.Content!.FileMetadata.LocalAppData.Tags, prepareTagsRequest.Tags);
        var latestLocalVersionTag = updatedFileResponse1.Content.FileMetadata.LocalAppData.VersionTag;

        //
        // Act - try to update the local metadata content only
        //
        const string expectedContent = "some content goes here";

        var request2 = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = latestLocalVersionTag,
            Content = expectedContent
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
        CollectionAssert.AreEquivalent(theUpdatedFile.FileMetadata.LocalAppData.Tags, prepareTagsRequest.Tags, "tags should not have changed");

        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.Content == expectedContent, "the content should have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task FailsWithBadRequestWhenInvalidLocalVersionTagSpecified(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        ClassicAssert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        const string content1 = "some local content here";
        const string content2 = "other content here";

        var request1 = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = content1
        };

        // first update - just use the owner api so we can prepare a file with a nonempty local version tag
        var prepareLocalMetadataResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataContent(request1);
        ClassicAssert.IsTrue(prepareLocalMetadataResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {prepareLocalMetadataResponse.StatusCode}");

        var r = prepareLocalMetadataResponse.Content;
        var expectedVersionTag = r.NewLocalVersionTag;

        //
        // Act - try to udpate the loca metadata with a bad local version tag
        //
        var request2 = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.NewGuid(), //random guid so it will fail
            Content = content2
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, "should have failed");

        // Get the file and see that it was not updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == expectedVersionTag);
        ClassicAssert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.Content == content1, "the content should not have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task FailsWithBadRequestWhenFileDoesNotExist(IApiClientContext callerContext, HttpStatusCode _)
    {
        //
        // Setup
        //
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        //
        // Act - try to update local metadata for non-existent file
        //
        var request = new UpdateLocalMetadataContentRequest()
        {
            File = new ExternalFileIdentifier()
            {
                FileId = Guid.NewGuid(), //random non-existent file
                TargetDrive = targetDrive
            },
            LocalVersionTag = Guid.Empty,
            Content = "some local content here"
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, "should have failed");
    }
}