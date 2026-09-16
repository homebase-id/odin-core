using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_Universal/DriveTests/LocalAppMetadata/LocalAppMetadataContentTests</c>. Owner seeds a
/// file (and, where the test needs it, a starting local-app-metadata content or tag set); the
/// caller-under-test then invokes <c>UpdateLocalAppMetadataContent</c> across the Owner / App /
/// Guest write-only matrix. Validates: initial set when not present, update with a valid local
/// version tag, tag preservation when only content changes, bad-request on a wrong version tag, and
/// bad-request on a nonexistent file.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> local-app-metadata endpoints through the in-process host: the V1-shaped
/// <see cref="UniversalDriveApiClient"/> is reused unchanged, resolved against the fixture's
/// <c>Factory</c> via <c>InProcessApiClientFactory</c>'s V1 path normalization. That makes them
/// distinct from the sibling <see cref="ContentTests"/>, which covers the same behaviour on the V2
/// endpoints.
/// </remarks>
[TestFixture]
public class V1ContentTests : V2Fixture
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

    public static IEnumerable<object[]> GuestWriteOnly()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestWriteOnly))]
    public async Task CanUpdateLocalAppMetadataContentWhenNotSetInTargetFile(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AccessControlList = AccessControlList.Authenticated;

        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content.File;

        // Act - update the local app metadata
        var callerDriveClient = caller.V1.Drive;

        const string content = "some local content here";
        var request = new UpdateLocalMetadataContentRequest()
        {
            File = targetFile,
            LocalVersionTag = Guid.Empty,
            Content = content
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //continue testing

        var result = response.Content;
        Assert.That(result.NewLocalVersionTag == Guid.Empty, Is.False);

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Content, Is.EqualTo(content));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanUpdateLocalAppMetadataContentWhenSetInTargetFileUsingValidLocalVersionTag(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
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
        var prepareLocalMetadataResponse = await ownerDriveClient.UpdateLocalAppMetadataContent(request1);
        Assert.That(prepareLocalMetadataResponse.StatusCode, Is.EqualTo(expectedStatusCode));

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse1.IsSuccessStatusCode, Is.True);
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

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Content, Is.EqualTo(content2), "the content should have changed");
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task TagsAreNotChangedWhenUpdatingLocalMetadataContent(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
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

        var prepareTagsResponse = await ownerDriveClient.UpdateLocalAppMetadataTags(prepareTagsRequest);
        Assert.That(prepareTagsResponse.IsSuccessStatusCode, Is.True);

        // get the updated file and read the version tag from there; to ensure a test closer to what the FE would do
        var updatedFileResponse1 = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse1.IsSuccessStatusCode, Is.True);
        Assert.That(updatedFileResponse1.Content!.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(prepareTagsRequest.Tags));
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

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        var result = response.Content;

        // Get the file and see that it was updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(result.NewLocalVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(prepareTagsRequest.Tags),
            "tags should not have changed");

        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Content, Is.EqualTo(expectedContent), "the content should have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task FailsWithBadRequestWhenInvalidLocalVersionTagSpecified(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var prepareFileResponse = await ownerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
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
        var prepareLocalMetadataResponse = await ownerDriveClient.UpdateLocalAppMetadataContent(request1);
        Assert.That(prepareLocalMetadataResponse.StatusCode, Is.EqualTo(expectedStatusCode));

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

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request2);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "should have failed");

        // Get the file and see that it was not updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(updatedFileResponse.IsSuccessStatusCode, Is.True);
        var theUpdatedFile = updatedFileResponse.Content;
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(expectedVersionTag));
        Assert.That(theUpdatedFile.FileMetadata.LocalAppData.Content, Is.EqualTo(content1), "the content should not have changed");
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task FailsWithBadRequestWhenFileDoesNotExist(CallerSpec spec, HttpStatusCode _)
    {
        //
        // Setup
        //
        var caller = await SetupCaller(spec);

        //
        // Act - try to update local metadata for non-existent file
        //
        var request = new UpdateLocalMetadataContentRequest()
        {
            File = new ExternalFileIdentifier()
            {
                FileId = Guid.NewGuid(), //random non-existent file
                TargetDrive = spec.TargetDrive
            },
            LocalVersionTag = Guid.Empty,
            Content = "some local content here"
        };

        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "should have failed");
    }
}
