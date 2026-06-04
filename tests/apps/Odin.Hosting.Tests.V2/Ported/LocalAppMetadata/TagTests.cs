using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_V2/Tests/Drive/LocalAppMetadata/LocalAppMetadataTagTests</c>. Owner seeds the file
/// (and optionally a starting set of tags / content) via V2 endpoints; the caller-under-test
/// invokes <c>UpdateLocalAppMetadataTags</c> across the Owner / App / Guest ReadWrite matrix.
/// Also covers the QueryBatch-by-local-tag paths (match-at-least-one, match-all).
/// </summary>
[TestFixture]
public class TagTests : V2Fixture
{
    public static IEnumerable<object[]> ReadWriteCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanUpdateLocalAppMetadataTagsWhenNotSetInTargetFile(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Authenticated;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        var tags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = tags });
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        Assert.That(response.Content!.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content.NewLocalVersionTag));
        Assert.That(header.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(tags));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanUpdateLocalAppMetadataTagsWhenSetInTargetFileUsingValidLocalVersionTag(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        var initialTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var seedResponse = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = initialTags });
        Assert.That(seedResponse.IsSuccessStatusCode, Is.True);

        var header1 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        var latestVersionTag = header1.FileMetadata.LocalAppData.VersionTag;

        var newTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = latestVersionTag, Tags = newTags });
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var header2 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header2.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(newTags));
        Assert.That(header2.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content!.NewLocalVersionTag));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task ContentDoesNotChangeWhenUpdatingTags(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        const string originalContent = "expected content";
        var seedContent = await owner.Drives.Writer.UpdateLocalAppMetadataContent(file.DriveId, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { LocalVersionTag = Guid.Empty, Content = originalContent });
        Assert.That(seedContent.IsSuccessStatusCode, Is.True);

        var header1 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header1.FileMetadata.LocalAppData.Content, Is.EqualTo(originalContent));
        var latestVersionTag = header1.FileMetadata.LocalAppData.VersionTag;

        var newTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var response = await caller.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = latestVersionTag, Tags = newTags });
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        var header2 = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header2.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(newTags));
        Assert.That(header2.FileMetadata.LocalAppData.Content, Is.EqualTo(originalContent), "content shouldn't change");
        Assert.That(header2.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(response.Content!.NewLocalVersionTag));
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

        var initialTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var seedResponse = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = initialTags });
        Assert.That(seedResponse.IsSuccessStatusCode, Is.True);
        var expectedVersionTag = seedResponse.Content!.NewLocalVersionTag;

        var response = await caller.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [Guid.NewGuid(), Guid.NewGuid()] });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        Assert.That(header.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(expectedVersionTag));
        Assert.That(header.FileMetadata.LocalAppData.Tags, Is.EquivalentTo(initialTags), "tags shouldn't change");
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task FailsWithBadRequestWhenFileDoesNotExist(CallerSpec spec, HttpStatusCode _)
    {
        var caller = await SetupCaller(spec);

        var response = await caller.Drives.Writer.UpdateLocalAppMetadataTags(spec.TargetDrive.Alias, Guid.NewGuid(),
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [Guid.NewGuid(), Guid.NewGuid()] });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanQueryBatchByLocalTagsMatchAtLeastOne(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();
        var seed = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [tag1, tag2, tag3] });
        Assert.That(seed.IsSuccessStatusCode, Is.True);

        var queryResponse = await caller.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = spec.TargetDrive, LocalTagsMatchAtLeastOne = [tag3] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(queryResponse.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        var hits = queryResponse.Content!.SearchResults.ToList();
        Assert.That(hits.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(hits.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanQueryBatchByLocalTagsMatchAll(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AccessControlList = AccessControlList.Anonymous;
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, fileMetadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var file = upload.Content!;

        var tag1 = Guid.NewGuid();
        var tag2 = Guid.NewGuid();
        var tag3 = Guid.NewGuid();
        var seed = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [tag1, tag2, tag3] });
        Assert.That(seed.IsSuccessStatusCode, Is.True);

        var queryResponse = await caller.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = spec.TargetDrive, LocalTagsMatchAll = [tag1, tag2] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(queryResponse.StatusCode, Is.EqualTo(expected));
        if (expected != HttpStatusCode.OK) return;

        var hits = queryResponse.Content!.SearchResults.ToList();
        Assert.That(hits.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag1)), Is.True);
        Assert.That(hits.Any(r => r.FileMetadata.LocalAppData.Tags.Contains(tag2)), Is.True);
    }
}
