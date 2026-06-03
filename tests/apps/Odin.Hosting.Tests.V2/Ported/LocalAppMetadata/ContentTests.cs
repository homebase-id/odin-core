using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_V2/Tests/Drive/LocalAppMetadata/LocalAppMetadataContentTests</c>. Owner seeds the
/// file (and optionally a starting local-app-metadata payload) via V2 endpoints; the caller-under-test
/// invokes <c>UpdateLocalAppMetadataContent</c> across the Owner / App / Guest ReadWrite matrix.
/// Validates: initial set when not present, idempotent re-set with a valid local version tag, tag
/// preservation when only content changes, bad-request on wrong version tag, bad-request on
/// nonexistent file.
/// </summary>
[TestFixture]
public class ContentTests : V2Fixture
{
    public static IEnumerable<object[]> ReadWriteCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanUpdateLocalAppMetadataContentWhenNotSetInTargetFile(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Authenticated;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        const string content = "some local content here";
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.Empty, Content = content });
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"actual {response.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        Assert.That(response.Content!.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content.NewLocalVersionTag));
        Assert.That(header.FileMetadata.LocalAppData.Content, Is.EqualTo(content));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanUpdateLocalAppMetadataContentWhenSetInTargetFileUsingValidLocalVersionTag(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        // Owner seeds an initial local-app-metadata content so the file has a non-empty version tag.
        var seedResponse = await owner.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.Empty, Content = "some local content here" });
        Assert.That(seedResponse.IsSuccessStatusCode, Is.True);

        var header1 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        var latestVersionTag = header1.FileMetadata.LocalAppData.VersionTag;

        const string updatedContent = "other info here";
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = latestVersionTag, Content = updatedContent });
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var header2 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header2.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content!.NewLocalVersionTag));
        Assert.That(header2.FileMetadata.LocalAppData.Content, Is.EqualTo(updatedContent));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task TagsAreNotChangedWhenUpdatingLocalMetadataContent(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var seedTagsResponse = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [tag1, tag2] });
        Assert.That(seedTagsResponse.IsSuccessStatusCode, Is.True);

        var header1 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header1.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(new[] { tag1, tag2 }));
        var latestVersionTag = header1.FileMetadata.LocalAppData.VersionTag;

        const string expectedContent = "some content goes here";
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = latestVersionTag, Content = expectedContent });
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var header2 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header2.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content!.NewLocalVersionTag));
        Assert.That(header2.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(new[] { tag1, tag2 }), "tags should be untouched");
        Assert.That(header2.FileMetadata.LocalAppData.Content, Is.EqualTo(expectedContent));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task FailsWithBadRequestWhenInvalidLocalVersionTagSpecified(CallerSpec spec, HttpStatusCode _)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        const string seedContent = "some local content here";
        var seedResponse = await owner.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.Empty, Content = seedContent });
        Assert.That(seedResponse.IsSuccessStatusCode, Is.True);
        var expectedVersionTag = seedResponse.Content!.NewLocalVersionTag;

        // Use a random (wrong) version tag — should 400 regardless of caller.
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.NewGuid(), Content = "other content here" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(expectedVersionTag));
        Assert.That(header.FileMetadata.LocalAppData.Content, Is.EqualTo(seedContent), "content shouldn't have changed");
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task FailsWithBadRequestWhenFileDoesNotExist(CallerSpec spec, HttpStatusCode _)
    {
        var caller = await SetupCaller(spec);

        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(spec.TargetDrive.Alias, Guid.NewGuid(),
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.Empty, Content = "some local content here" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
