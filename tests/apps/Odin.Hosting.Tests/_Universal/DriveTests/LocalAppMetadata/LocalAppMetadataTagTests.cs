using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;

namespace Odin.Hosting.Tests._Universal.DriveTests.LocalAppMetadata;

public class LocalAppMetadataTests
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

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }
    
    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateLocalAppMetadataTagsWhenNotSetInTargetFile(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var result = response.Content;
            Assert.IsFalse(result.NewLocalVersionTag == Guid.Empty);

            // Get the file and see that it's updated
            var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
            var theUpdatedFile = updatedFileResponse.Content;
            Assert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
            CollectionAssert.AreEquivalent(theUpdatedFile.FileMetadata.LocalAppData.Tags, request.Tags);
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
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        var tag1r1 = Guid.NewGuid();
        var tag2r1 = Guid.NewGuid();
        var request1 = new UpdateLocalMetadataTagsRequest
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1r1, tag2r1]
        };

        // first update - just use the owner api so we can prepare a file with a nonempty local version tag
        var prepareLocalMetadataResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataTags(request1);
        Assert.IsTrue(prepareLocalMetadataResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {prepareLocalMetadataResponse.StatusCode}");

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(updatedFileResponse1.IsSuccessStatusCode);
        var latestLocalVersionTag = updatedFileResponse1.Content.FileMetadata.LocalAppData.VersionTag;

        //
        // Act - try to update the local metadata tags
        //
        var tag1r2 = Guid.NewGuid();
        var tag2r2 = Guid.NewGuid();
        var request2 = new UpdateLocalMetadataTagsRequest
        {
            File = targetFile,
            LocalVersionTag = latestLocalVersionTag,
            Tags = [tag1r2, tag2r2]
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        CollectionAssert.AreEquivalent(theUpdatedFile.FileMetadata.LocalAppData.Tags, request2.Tags);
        Assert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task ContentDoesNotChangeWhenUpdatingTags(IApiClientContext callerContext,
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
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        // first set some content
        const string originalContent = "expected content";
        var updateContentResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataContent(new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = originalContent
        });
        Assert.IsTrue(updateContentResponse.IsSuccessStatusCode);

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        // validate the content is set
        var updatedFileResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(updatedFileResponse1.IsSuccessStatusCode);
        Assert.IsTrue(updatedFileResponse1.Content!.FileMetadata.LocalAppData.Content == originalContent);

        var latestLocalVersionTag = updatedFileResponse1.Content.FileMetadata.LocalAppData.VersionTag;

        //
        // Act - update the local metadata tags
        //
        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request2 = new UpdateLocalMetadataTagsRequest
        {
            File = targetFile,
            LocalVersionTag = latestLocalVersionTag,
            Tags = [tag1, tag2]
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        CollectionAssert.AreEquivalent(theUpdatedFile.FileMetadata.LocalAppData.Tags, request2.Tags);
        Assert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.Content == originalContent, "the original content should not have changed");
        Assert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == result.NewLocalVersionTag);
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
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request1 = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        // first update - just use the owner api so we can prepare a file with a nonempty local version tag
        var prepareLocalMetadataResponse = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataTags(request1);
        Assert.IsTrue(prepareLocalMetadataResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {prepareLocalMetadataResponse.StatusCode}");

        var r = prepareLocalMetadataResponse.Content;
        var expectedVersionTag = r.NewLocalVersionTag;

        var tag1r2 = Guid.NewGuid();
        var tag2r2 = Guid.NewGuid();
        var request2 = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1r2, tag2r2]
        };
        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, "should have failed");

        // Get the file and see that it was not updated
        var updatedFileResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        Assert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.IsTrue(theUpdatedFile.FileMetadata.LocalAppData.VersionTag == expectedVersionTag);
        CollectionAssert.AreEquivalent(theUpdatedFile.FileMetadata.LocalAppData.Tags, request1.Tags, "the content should not have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
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
        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = new ExternalFileIdentifier()
            {
                FileId = Guid.NewGuid(), //random non-existent file
                TargetDrive = targetDrive
            },
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, "should have failed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryBatchByLocalTagsMatchAtLeastOne(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var response = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataTags(request);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var qbr = new QueryBatchRequest
        {
            QueryParams = new FileQueryParams()
            {
                LocalTagsMatchAtLeastOne = [tag1, tag2]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var queryBatchResponse = await callerDriveClient.QueryBatch(qbr);
        Assert.IsTrue(queryBatchResponse.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var result = response.Content;
            Assert.IsFalse(result.NewLocalVersionTag == Guid.Empty);
            var searchResults = queryBatchResponse.Content.SearchResults.ToList();
            Assert.IsTrue(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)));
            Assert.IsTrue(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)));
        }
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryBatchByLocalTagsMatchAll(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(prepareFileResponse.IsSuccessStatusCode);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var response = await ownerApiClient.DriveRedux.UpdateLocalAppMetadataTags(request);
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var qbr = new QueryBatchRequest
        {
            QueryParams = new FileQueryParams()
            {
                LocalTagsMatchAll = [tag1, tag2]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var queryBatchResponse = await callerDriveClient.QueryBatch(qbr);
        Assert.IsTrue(queryBatchResponse.StatusCode == expectedStatusCode);

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var result = response.Content;
            Assert.IsFalse(result.NewLocalVersionTag == Guid.Empty);
            var searchResults = queryBatchResponse.Content.SearchResults.ToList();
            Assert.IsTrue(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)));
            Assert.IsTrue(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)));
        }
    }
}