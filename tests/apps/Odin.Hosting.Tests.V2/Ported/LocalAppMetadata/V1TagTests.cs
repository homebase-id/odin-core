using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_Universal/DriveTests/LocalAppMetadata/LocalAppMetadataTagTests</c>. Owner seeds the
/// file (and where the test needs one, a starting set of tags / content); the caller-under-test then
/// updates the local app metadata tags across the Owner / App / Guest matrix. Also covers the
/// QueryBatch- and QueryModified-by-local-tag paths (match-at-least-one, match-all).
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> local-app-metadata endpoints through the in-process host: the V1-shaped
/// <see cref="UniversalDriveApiClient"/> is reused unchanged, resolved against the fixture's
/// <c>Factory</c> via <c>InProcessApiClientFactory</c>'s V1 path normalization. That makes them
/// distinct from the sibling <c>TagTests</c> in this folder, which covers the same behaviour on the
/// V2 endpoints.
/// </remarks>
[TestFixture]
public class V1TagTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Pippin];

    public static IEnumerable<object[]> OwnerAllowed()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> AppAllowed()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> GuestNotAllowed()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> GuestAllowed()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateLocalAppMetadataTagsWhenNotSetInTargetFile(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;

        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(request.Tags));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task CanUpdateLocalAppMetadataContentWhenSetInTargetFileUsingValidLocalVersionTag(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
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
        var prepareLocalMetadataResponse = await ownerDriveClient.UpdateLocalAppMetadataTags(request1);
        Assert.That(prepareLocalMetadataResponse.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {prepareLocalMetadataResponse.StatusCode}");

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse1.IsSuccessStatusCode, Is.True);
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

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(request2.Tags));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task ContentDoesNotChangeWhenUpdatingTags(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        // first set some content
        const string originalContent = "expected content";
        var updateContentResponse = await ownerDriveClient.UpdateLocalAppMetadataContent(new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = originalContent
        });
        Assert.That(updateContentResponse.IsSuccessStatusCode, Is.True);

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        // validate the content is set
        var updatedFileResponse1 = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse1.IsSuccessStatusCode, Is.True);
        Assert.That(updatedFileResponse1.Content!.FileMetadata.LocalAppData.Content, Is.EqualTo(originalContent));

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

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(request2.Tags));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Content, Is.EqualTo(originalContent),
            "the original content should not have changed");
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task FailsWithBadRequestWhenInvalidLocalVersionTagSpecified(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
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
        var prepareLocalMetadataResponse = await ownerDriveClient.UpdateLocalAppMetadataTags(request1);
        Assert.That(prepareLocalMetadataResponse.StatusCode, Is.EqualTo(expectedStatusCode),
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
        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request2);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "should have failed");

        // Get the file and see that it was not updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(expectedVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(request1.Tags),
            "the content should not have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    // [TestCaseSource(nameof(GuestNotAllowed))] //not required in this test
    public async Task FailsWithBadRequestWhenFileDoesNotExist(CallerSpec spec, HttpStatusCode _)
    {
        //
        // Setup
        //
        var caller = await SetupCaller(spec, Identities.Pippin);

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
                TargetDrive = spec.TargetDrive
            },
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2]
        };

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var response = await callerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "should have failed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryBatchByLocalTagsMatchAtLeastOne(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Anonymous;
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2, tag3]
        };

        var response = await ownerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var qbr = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                LocalTagsMatchAtLeastOne = [tag3]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var queryBatchResponse = await callerDriveClient.QueryBatch(qbr);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));
        var searchResults = queryBatchResponse.Content.SearchResults.ToList();
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryBatchByLocalTagsMatchAll(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Anonymous;
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();

        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2, tag3]
        };

        var response = await ownerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var qbr = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                LocalTagsMatchAll = [tag1, tag2]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        var queryBatchResponse = await callerDriveClient.QueryBatch(qbr);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));
        var searchResults = queryBatchResponse.Content.SearchResults.ToList();
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryModifiedByLocalTagsMatchAtLeastOne(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Anonymous;
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();
        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2, tag3]
        };

        var response = await ownerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var qmr = new QueryModifiedRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                LocalTagsMatchAtLeastOne = [tag3]
            },
            ResultOptions = new QueryModifiedResultOptions()
            {
                MaxRecords = 10,
                MaxDate = UnixTimeUtc.Now().AddHours(1).milliseconds,
                IncludeHeaderContent = true
            }
        };

        await Task.Delay(5);

        var queryBatchResponse = await callerDriveClient.QueryModified(qmr);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));
        var searchResults = queryBatchResponse.Content.SearchResults.ToList();
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanQueryModifiedByLocalTagsMatchAll(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec, Identities.Pippin);
        var ownerDriveClient = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Anonymous;
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();

        var request = new UpdateLocalMetadataTagsRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Tags = [tag1, tag2, tag3]
        };

        var response = await ownerDriveClient.UpdateLocalAppMetadataTags(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode),
            $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        // Act - update the local app metadata

        var callerDriveClient = new UniversalDriveApiClient(caller.Identity, caller.Factory);
        var qmr = new QueryModifiedRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                LocalTagsMatchAtLeastOne = [tag3]
            },
            ResultOptions = new QueryModifiedResultOptions()
            {
                MaxRecords = 10,
                MaxDate = UnixTimeUtc.Now().AddHours(1).milliseconds,
                IncludeHeaderContent = true
            }
        };

        await Task.Delay(5);

        var queryBatchResponse = await callerDriveClient.QueryModified(qmr);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));
        var searchResults = queryBatchResponse.Content.SearchResults.ToList();
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(searchResults.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }
}
