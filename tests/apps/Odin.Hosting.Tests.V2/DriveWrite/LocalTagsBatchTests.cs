using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// <c>POST files/update-local-tags-batch</c>: adds and removes local tags on many files, keeping each file's other local
/// tags and local data, conditional on a file's local version tag only when the caller sends one.
/// </summary>
[TestFixture]
public class LocalTagsBatchTests : V2Fixture
{
    private static readonly Guid Favorite = Guid.NewGuid();
    private static readonly Guid Kept = Guid.NewGuid();
    private static readonly Guid Gone = Guid.NewGuid();

    public static IEnumerable<object[]> CallerCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task AddsAndRemovesOnlyTheNamedTags(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var tagged = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "tagged");
        var untagged = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "untagged");
        await SetLocalTagsAsync(owner, spec.TargetDrive, tagged, Kept, Gone);
        var before = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, tagged.FileId);

        var response = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias,
            Batch([Favorite], [Gone], (tagged.FileId, null), (untagged.FileId, null)));

        Assert.That(response.StatusCode, Is.EqualTo(expected), response.Error?.Content);
        if (expected != HttpStatusCode.OK)
        {
            Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, tagged), Is.EqualTo(new[] { Kept, Gone }));
            return;
        }

        var results = response.Content!.Results;
        Assert.That(results.Select(r => (r.FileId, r.ErrorCode)),
            Is.EqualTo(new[] { (tagged.FileId, (OdinClientErrorCode?)null), (untagged.FileId, null) }));

        var after = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, tagged.FileId);
        Assert.That(after.FileMetadata.LocalAppData.Tags, Is.EqualTo(new[] { Kept, Favorite }));
        Assert.That(after.FileMetadata.LocalAppData.VersionTag, Is.EqualTo(results[0].NewLocalVersionTag));
        Assert.That(after.FileMetadata.LocalAppData.VersionTag, Is.Not.EqualTo(before.FileMetadata.LocalAppData.VersionTag));
        Assert.That(results[0].Updated?.milliseconds, Is.EqualTo(after.FileMetadata.Updated.milliseconds));
        Assert.That(after.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds));
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag), "the file's own version tag stays");
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo(before.FileMetadata.AppData.Content));

        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, untagged), Is.EqualTo(new[] { Favorite }));
    }

    [Test]
    public async Task WithALocalVersionTagTheChangeIsConditional()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var neverWritten = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "never written");
        var current = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "current");
        var stale = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "stale");
        var currentTag = await SetLocalTagsAsync(owner, spec.TargetDrive, current, Kept);
        var staleTag = await SetLocalTagsAsync(owner, spec.TargetDrive, stale, Kept);

        var response = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias, Batch([Favorite], [],
            (neverWritten.FileId, Guid.Empty),
            (current.FileId, currentTag),
            (stale.FileId, Guid.NewGuid())));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode, r.NewLocalVersionTag.HasValue)), Is.EqualTo(new[]
        {
            (neverWritten.FileId, (OdinClientErrorCode?)null, true),
            (current.FileId, null, true),
            (stale.FileId, OdinClientErrorCode.VersionTagMismatch, false)
        }));

        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, neverWritten), Is.EqualTo(new[] { Favorite }));
        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, current), Is.EqualTo(new[] { Kept, Favorite }));
        var staleHeader = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, stale.FileId);
        Assert.That((staleHeader.FileMetadata.LocalAppData.Tags, staleHeader.FileMetadata.LocalAppData.VersionTag),
            Is.EqualTo((new List<Guid> { Kept }, staleTag)));
    }

    [Test]
    public async Task AChangeThatLeavesTheTagsAsTheyAreWritesNothing()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "already a favorite");
        var localTag = await SetLocalTagsAsync(owner, spec.TargetDrive, file, Favorite);
        var before = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, file.FileId);

        var response = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias,
            Batch([Favorite], [Gone], (file.FileId, null)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        var result = response.Content!.Results.Single();
        Assert.That((result.ErrorCode, result.NewLocalVersionTag, result.Updated?.milliseconds),
            Is.EqualTo(((OdinClientErrorCode?)null, (Guid?)localTag, (long?)before.FileMetadata.Updated.milliseconds)));
        var after = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, file.FileId);
        Assert.That((after.FileMetadata.LocalAppData.VersionTag, after.FileMetadata.Updated.milliseconds),
            Is.EqualTo((localTag, before.FileMetadata.Updated.milliseconds)));
    }

    [Test]
    public async Task ConcurrentChangesWithoutALocalVersionTagLoseNoTag()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "two devices");
        var tags = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();

        var responses = await Task.WhenAll(tags.Select(tag =>
            caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias, Batch([tag], [], (file.FileId, null)))));

        Assert.That(responses.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.OK), responses.FirstOrDefault(r => r.Error != null)?.Error?.Content);
        Assert.That(responses.Select(r => r.Content!.Results.Single().ErrorCode), Is.All.Null,
            "a change without a local version tag is read and written again until it gets in");
        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, file), Is.EquivalentTo(tags), "no change lost another's tag");
    }

    [Test]
    public async Task TaggingACommentLeavesItsPostAlone()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var post = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "post");
        var comments = new DriveWriterV2Client(owner.Identity, owner.Factory, FileSystemType.Comment);
        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "a comment", AccessControlList.Anonymous);
        metadata.ReferencedFile = new GlobalTransitIdFileIdentifier { GlobalTransitId = post.GlobalTransitId!.Value, TargetDrive = spec.TargetDrive };
        var comment = await comments.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, new UploadManifest { PayloadDescriptors = [] }, []);
        Assert.That(comment.IsSuccessStatusCode, Is.True, $"comment upload failed: {comment.StatusCode} {comment.Error?.Content}");
        var before = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, post.FileId);

        var response = await comments.UpdateLocalTagsBatch(spec.TargetDrive.Alias, Batch([Favorite], [], (comment.Content!.FileId, null)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Single().ErrorCode, Is.Null);
        var after = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, post.FileId);
        Assert.That((after.FileMetadata.Updated.milliseconds, after.FileMetadata.VersionTag),
            Is.EqualTo((before.FileMetadata.Updated.milliseconds, before.FileMetadata.VersionTag)),
            "local data changes no reaction preview, so the post is not written");
    }

    [Test]
    public async Task OneBadFileDoesNotBlockTheRest()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var full = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "full");
        var deleted = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "deleted");
        var current = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "current");
        await SetLocalTagsAsync(owner, spec.TargetDrive, full, Enumerable.Range(0, LocalAppMetadata.MaxTagCount).Select(_ => Guid.NewGuid()).ToArray());
        await AppFileUploads.SoftDeleteAsync(owner, deleted);
        var missing = Guid.NewGuid();

        var response = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias, Batch([Favorite], [],
            (missing, null),
            (full.FileId, null),
            (deleted.FileId, null),
            (current.FileId, null)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (missing, (OdinClientErrorCode?)OdinClientErrorCode.FileNotFound),
            (full.FileId, OdinClientErrorCode.ArgumentError),
            (deleted.FileId, null),
            (current.FileId, null)
        }));

        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, full), Has.Count.EqualTo(LocalAppMetadata.MaxTagCount));
        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, deleted), Is.EqualTo(new[] { Favorite }),
            "local tags outlive a soft delete, so a tombstone's may change");
        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, current), Is.EqualTo(new[] { Favorite }));
    }

    [Test]
    public async Task AFileHiddenByItsAclIsNotFoundAndDoesNotStopTheRest()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var hidden = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "owner only", AccessControlList.OwnerOnly);
        var visible = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "visible");

        var response = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias,
            Batch([Favorite], [], (hidden.FileId, null), (visible.FileId, null)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (hidden.FileId, (OdinClientErrorCode?)OdinClientErrorCode.FileNotFound),
            (visible.FileId, null)
        }));
        Assert.That(await LocalTagsAsync(owner, spec.TargetDrive, hidden), Is.Empty);
    }

    [Test]
    public async Task QueriesByLocalTagFollowTheChanges()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var a = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "a");
        var b = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "b");

        var add = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias,
            Batch([Favorite], [], (a.FileId, null), (b.FileId, null)));
        Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.OK), add.Error?.Content);
        Assert.That(await AppFileUploads.QueryFileIdsAsync(owner, new FileQueryParamsV1 { TargetDrive = spec.TargetDrive, LocalTagsMatchAtLeastOne = [Favorite] }), Is.EquivalentTo(new[] { a.FileId, b.FileId }));

        var remove = await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias, Batch([], [Favorite], (a.FileId, null)));
        Assert.That(remove.StatusCode, Is.EqualTo(HttpStatusCode.OK), remove.Error?.Content);
        Assert.That(await AppFileUploads.QueryFileIdsAsync(owner, new FileQueryParamsV1 { TargetDrive = spec.TargetDrive, LocalTagsMatchAtLeastOne = [Favorite] }), Is.EqualTo(new[] { b.FileId }));
    }

    [Test]
    public async Task RejectsMalformedBatches()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, _) = await SetupCallerWithOwner(spec);
        var duplicate = Guid.NewGuid();
        var batches = new Dictionary<string, UpdateLocalTagsBatchRequestV2>
        {
            ["no files"] = Batch([Favorite], []),
            ["too many"] = Batch([Favorite], [], Enumerable.Range(0, FileVersionTagBatchV2.MaxFiles + 1)
                .Select(_ => (Guid.NewGuid(), (Guid?)null)).ToArray()),
            ["duplicate"] = Batch([Favorite], [], (duplicate, null), (duplicate, null)),
            ["null entry"] = new UpdateLocalTagsBatchRequestV2 { AddTags = [Favorite], Files = [null] },
            ["no tags"] = Batch([], [], (Guid.NewGuid(), null)),
            ["empty tag"] = Batch([Guid.Empty], [], (Guid.NewGuid(), null)),
            ["added and removed"] = Batch([Favorite], [Favorite], (Guid.NewGuid(), null)),
            ["too many tags"] = Batch(Enumerable.Range(0, LocalAppMetadata.MaxTagCount + 1).Select(_ => Guid.NewGuid()).ToArray(), [],
                (Guid.NewGuid(), null)),
        };

        var statuses = new Dictionary<string, HttpStatusCode>();
        foreach (var (name, batch) in batches)
        {
            statuses[name] = (await caller.Drives.Writer.UpdateLocalTagsBatch(spec.TargetDrive.Alias, batch)).StatusCode;
        }

        Assert.That(statuses, Is.EqualTo(batches.Keys.ToDictionary(k => k, _ => HttpStatusCode.BadRequest)));
    }

    private static UpdateLocalTagsBatchRequestV2 Batch(Guid[] add, Guid[] remove, params (Guid FileId, Guid? LocalVersionTag)[] files) => new()
    {
        AddTags = add.ToList(),
        RemoveTags = remove.ToList(),
        Files = files.Select(f => new FileLocalVersionTagV2 { FileId = f.FileId, LocalVersionTag = f.LocalVersionTag }).ToList()
    };

    /// <summary>
    /// Sets the file's local tags through the single-file route, answering its new local version tag
    /// </summary>
    private static async Task<Guid> SetLocalTagsAsync(OwnerSession owner, TargetDrive drive, CreateFileResult file, params Guid[] tags)
    {
        var header = await AppFileUploads.HeaderAsync(owner, drive, file.FileId);
        var response = await owner.Drives.Writer.UpdateLocalAppMetadataTags(file.DriveId, file.FileId, new UpdateLocalMetadataTagsRequestV2
        {
            LocalVersionTag = header.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty,
            Tags = tags.ToList()
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"local tags failed: {response.StatusCode} {response.Error?.Content}");
        return response.Content!.NewLocalVersionTag;
    }

    /// <summary>
    /// The file's local tags as the owner reads them, empty when its local data was never written
    /// </summary>
    private static async Task<List<Guid>> LocalTagsAsync(OwnerSession owner, TargetDrive drive, CreateFileResult file) =>
        (await AppFileUploads.HeaderAsync(owner, drive, file.FileId)).FileMetadata.LocalAppData?.Tags ?? [];
}
