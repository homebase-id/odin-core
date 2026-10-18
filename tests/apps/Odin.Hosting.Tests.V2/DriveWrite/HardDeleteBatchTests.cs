using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// <c>POST files/hard-delete-batch</c>: deletes each file for good (active or a tombstone), only at the version tag the
/// caller names, per file, leaving nothing in the change feed.
/// </summary>
[TestFixture]
public class HardDeleteBatchTests : V2Fixture
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
    public async Task DeletesTheFilesForGood(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var a = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "a");
        var b = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "b");

        var response = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias,
            Batch((a.FileId, a.NewVersionTag), (b.FileId, b.NewVersionTag)));

        Assert.That(response.StatusCode, Is.EqualTo(expected), response.Error?.Content);
        var remaining = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        if (expected != HttpStatusCode.OK)
        {
            Assert.That(remaining.Keys, Is.EquivalentTo(new[] { a.FileId, b.FileId }));
            return;
        }

        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)),
            Is.EqualTo(new[] { (a.FileId, (OdinClientErrorCode?)null), (b.FileId, null) }));
        Assert.That(remaining, Is.Empty, "a hard delete leaves nothing in the feed, not even a tombstone");
    }

    [Test]
    public async Task EachFileIsDeletedOnlyAtItsVersionTag()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var current = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "current");
        var stale = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "stale");
        var tombstone = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "tombstone");
        var staleTombstone = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "stale tombstone");
        await AppFileUploads.SoftDeleteAsync(owner, tombstone);
        await AppFileUploads.SoftDeleteAsync(owner, staleTombstone);
        var feed = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        var missing = Guid.NewGuid();

        var response = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias, Batch(
            (current.FileId, current.NewVersionTag),
            (stale.FileId, Guid.NewGuid()),
            (missing, Guid.NewGuid()),
            (tombstone.FileId, feed[tombstone.FileId].VersionTag),
            (staleTombstone.FileId, staleTombstone.NewVersionTag))); // its tag from before the soft delete

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (current.FileId, (OdinClientErrorCode?)null),
            (stale.FileId, OdinClientErrorCode.VersionTagMismatch),
            (missing, OdinClientErrorCode.FileNotFound),
            (tombstone.FileId, null),
            (staleTombstone.FileId, OdinClientErrorCode.VersionTagMismatch),
        }));

        var after = await AppFileUploads.FeedAsync(owner, spec.TargetDrive);
        Assert.That(after.ToDictionary(kv => kv.Key, kv => kv.Value.FileState), Is.EquivalentTo(new Dictionary<Guid, FileState>
        {
            [stale.FileId] = FileState.Active,
            [staleTombstone.FileId] = FileState.Deleted,
        }));
        Assert.That(after[stale.FileId].VersionTag, Is.EqualTo(stale.NewVersionTag), "a refused file is left untouched");
    }

    [Test]
    public async Task ARetryReportsTheDeletedFilesAsNotFound()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "once");
        var batch = Batch((file.FileId, file.NewVersionTag));

        var first = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias, batch);
        var retry = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias, batch);

        Assert.That((first.StatusCode, retry.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)), retry.Error?.Content);
        Assert.That((first.Content!.Results.Single().ErrorCode, retry.Content!.Results.Single().ErrorCode),
            Is.EqualTo(((OdinClientErrorCode?)null, (OdinClientErrorCode?)OdinClientErrorCode.FileNotFound)));
    }

    [Test]
    public async Task AFileHiddenByItsAclIsNotFoundAndStays()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var visible = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "visible");
        var hidden = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "owner only", AccessControlList.OwnerOnly);

        var response = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias, Batch(
            (hidden.FileId, hidden.NewVersionTag),
            (visible.FileId, visible.NewVersionTag)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (hidden.FileId, (OdinClientErrorCode?)OdinClientErrorCode.FileNotFound),
            (visible.FileId, null)
        }));
        Assert.That((await AppFileUploads.FeedAsync(owner, spec.TargetDrive)).Keys, Is.EqualTo(new[] { hidden.FileId }));
    }

    [Test]
    public async Task RejectsAMalformedBatch()
    {
        // The full matrix of malformed batches is in ArchivalStatusBatchTests; both endpoints share the validator
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, _) = await SetupCallerWithOwner(spec);
        var duplicate = Guid.NewGuid();

        var response = await caller.Drives.Writer.HardDeleteBatch(spec.TargetDrive.Alias,
            Batch((duplicate, Guid.NewGuid()), (duplicate, Guid.NewGuid())));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
    }

    private static HardDeleteBatchRequestV2 Batch(params (Guid FileId, Guid VersionTag)[] files) => new()
    {
        Files = files.Select(f => new FileVersionTagV2 { FileId = f.FileId, VersionTag = f.VersionTag }).ToList()
    };
}
