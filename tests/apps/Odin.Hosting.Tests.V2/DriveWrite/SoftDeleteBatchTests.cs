using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// <c>POST files/soft-delete-batch</c>: deletes each file, leaving a tombstone, optionally only at the version tag the caller
/// names, per file. Recipients are covered in <see cref="Odin.Hosting.Tests.V2.Peer.SoftDeleteBatchPeerTests"/>.
/// </summary>
[TestFixture]
public class SoftDeleteBatchTests : V2Fixture
{
    public static IEnumerable<object[]> CallerCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task DeletesTheFilesAndAnswersTheFeedsTime(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var a = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "a");
        var b = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "b");

        var response = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias,
            Batch(File(a.FileId, a.NewVersionTag), File(b.FileId)));

        Assert.That(response.StatusCode, Is.EqualTo(expected), response.Error?.Content);
        var feed = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        if (expected != HttpStatusCode.OK)
        {
            Assert.That(feed.Values.Select(f => f.FileState), Is.All.EqualTo(FileState.Active));
            return;
        }

        var results = response.Content!.Results;
        Assert.That(results.Select(r => (r.FileId, r.ErrorCode, r.RecipientStatus.Count)),
            Is.EqualTo(new[] { (a.FileId, (OdinClientErrorCode?)null, 0), (b.FileId, null, 0) }));
        Assert.That(results.Select(r => (r.FileId, r.Updated)),
            Is.EqualTo(new[] { a, b }.Select(f => (f.FileId, (UnixTimeUtc?)feed[f.FileId].Updated))),
            "updated is the tombstone's time in the feed");
        Assert.That(feed.Values.Select(f => f.FileState), Is.All.EqualTo(FileState.Deleted));
    }

    [Test]
    public async Task EachFileAnswersForItself()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var current = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "current");
        var stale = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "stale");
        var untagged = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "untagged");
        var tombstone = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "tombstone");
        await AppFileUploads.SoftDeleteAsync(owner, tombstone);
        var tombstoneBefore = (await AppFileUploads.FeedAsync(owner, spec.TargetDrive))[tombstone.FileId];
        var missing = Guid.NewGuid();

        var response = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias, Batch(
            File(current.FileId, current.NewVersionTag),
            File(stale.FileId, Guid.NewGuid()),
            File(untagged.FileId),
            File(missing, Guid.NewGuid()),
            File(tombstone.FileId, tombstone.NewVersionTag))); // its tag from before the delete

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        var results = response.Content!.Results;
        Assert.That(results.Select(r => (r.FileId, r.ErrorCode, r.Updated.HasValue)), Is.EqualTo(new[]
        {
            (current.FileId, (OdinClientErrorCode?)null, true),
            (stale.FileId, OdinClientErrorCode.VersionTagMismatch, false),
            (untagged.FileId, null, true),
            (missing, OdinClientErrorCode.FileNotFound, false),
            (tombstone.FileId, null, true),
        }));

        var after = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        Assert.That(after.ToDictionary(kv => kv.Key, kv => kv.Value.FileState), Is.EquivalentTo(new Dictionary<Guid, FileState>
        {
            [current.FileId] = FileState.Deleted,
            [stale.FileId] = FileState.Active,
            [untagged.FileId] = FileState.Deleted,
            [tombstone.FileId] = FileState.Deleted,
        }));
        Assert.That(after[stale.FileId].VersionTag, Is.EqualTo(stale.NewVersionTag), "a refused file is left untouched");
        Assert.That((after[tombstone.FileId].VersionTag, after[tombstone.FileId].Updated, results[4].Updated),
            Is.EqualTo((tombstoneBefore.VersionTag, tombstoneBefore.Updated, (UnixTimeUtc?)tombstoneBefore.Updated)),
            "an existing tombstone is left as it is and answers its own time");
    }

    [Test]
    public async Task ARetryAnswersTheSameAndChangesNothing()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "once");
        var batch = Batch(File(file.FileId, file.NewVersionTag));

        var first = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias, batch);
        var retry = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias, batch);

        Assert.That((first.StatusCode, retry.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)), retry.Error?.Content);
        var (once, again) = (first.Content!.Results.Single(), retry.Content!.Results.Single());
        Assert.That((once.ErrorCode, again.ErrorCode, again.Updated), Is.EqualTo(((OdinClientErrorCode?)null,
            (OdinClientErrorCode?)null, once.Updated)));
    }

    [Test]
    public async Task AFileHiddenByItsAclIsNotFoundAndStays()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var visible = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "visible");
        var hidden = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "owner only", AccessControlList.OwnerOnly);

        var response = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias,
            Batch(File(hidden.FileId), File(visible.FileId)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (hidden.FileId, (OdinClientErrorCode?)OdinClientErrorCode.FileNotFound),
            (visible.FileId, null)
        }));
        var feed = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        Assert.That(feed[hidden.FileId].FileState, Is.EqualTo(FileState.Active));
    }

    public static IEnumerable<TestCaseData> MalformedBatches()
    {
        var dup = Guid.NewGuid();
        yield return new TestCaseData(new List<SoftDeleteFileV2>()).SetName("no files");
        yield return new TestCaseData(new List<SoftDeleteFileV2> { File(dup), File(dup) }).SetName("a file twice");
        yield return new TestCaseData(new List<SoftDeleteFileV2> { File(Guid.Empty) }).SetName("an empty fileId");
        yield return new TestCaseData(new List<SoftDeleteFileV2> { File(Guid.NewGuid(), Guid.Empty) }).SetName("an empty versionTag");
        yield return new TestCaseData(new List<SoftDeleteFileV2> { File(Guid.NewGuid(), recipients: ["not a domain"]) })
            .SetName("a malformed recipient");
        yield return new TestCaseData(new List<SoftDeleteFileV2> { File(Guid.NewGuid(), recipients: [null]) })
            .SetName("a null recipient");
        yield return new TestCaseData(Enumerable.Range(0, FileVersionTagBatchV2.MaxFiles + 1).Select(_ => File(Guid.NewGuid())).ToList())
            .SetName("too many files");
    }

    [Test, TestCaseSource(nameof(MalformedBatches))]
    public async Task AMalformedBatchIsRefusedWholeAndTouchesNothing(List<SoftDeleteFileV2> malformed)
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        // A good file ahead of the bad one shows nothing ran; a batch refused for its size has no file to put first
        var sized = malformed.Count is 0 or > FileVersionTagBatchV2.MaxFiles;
        var good = sized ? null : await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "first in the batch");
        var files = sized ? malformed : [File(good!.FileId), .. malformed];

        var response = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias, new SoftDeleteBatchRequestV2 { Files = files });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        if (good != null)
        {
            Assert.That((await AppFileUploads.FeedAsync(owner, spec.TargetDrive))[good.FileId].FileState, Is.EqualTo(FileState.Active));
        }
    }

    [Test]
    public async Task TheOwnIdentityIsRefusedAsARecipient()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "to myself");

        var response = await caller.Drives.Writer.SoftDeleteBatch(spec.TargetDrive.Alias,
            Batch(File(file.FileId, recipients: [owner.Identity])));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That((await AppFileUploads.FeedAsync(owner, spec.TargetDrive))[file.FileId].FileState, Is.EqualTo(FileState.Active));
    }

    internal static SoftDeleteFileV2 File(Guid fileId, Guid? versionTag = null, List<string> recipients = null) =>
        new() { FileId = fileId, VersionTag = versionTag, Recipients = recipients };

    internal static SoftDeleteBatchRequestV2 Batch(params SoftDeleteFileV2[] files) => new() { Files = files.ToList() };
}
