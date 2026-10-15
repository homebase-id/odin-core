using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
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
/// <c>POST files/update-archival-status-batch</c>: sets only appData.archivalStatus, per file, guarded by each
/// file's version tag, without the caller re-sending the header.
/// </summary>
[TestFixture]
public class ArchivalStatusBatchTests : V2Fixture
{
    public static IEnumerable<object[]> CallerCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task SetsOnlyTheArchivalStatus(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var a = await UploadAsync(owner, spec.TargetDrive, "content a");
        var b = await UploadAsync(owner, spec.TargetDrive, "content b");
        var before = await GetHeaderAsync(owner, a);

        var response = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias,
            Batch(2, (a.FileId, a.NewVersionTag), (b.FileId, b.NewVersionTag)));

        Assert.That(response.StatusCode, Is.EqualTo(expected), response.Error?.Content);
        if (expected != HttpStatusCode.OK)
        {
            Assert.That((await GetHeaderAsync(owner, a)).FileMetadata.AppData.ArchivalStatus, Is.EqualTo(0));
            return;
        }

        var results = response.Content!.Results;
        Assert.That(results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[] { (a.FileId, (OdinClientErrorCode?)null), (b.FileId, null) }));

        var after = await GetHeaderAsync(owner, a);
        Assert.That(after.FileMetadata.AppData.ArchivalStatus, Is.EqualTo(2));
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(results[0].NewVersionTag));
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(before.FileMetadata.VersionTag));
        Assert.That(after.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds));
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo(before.FileMetadata.AppData.Content));
        Assert.That(after.FileMetadata.AppData.FileType, Is.EqualTo(before.FileMetadata.AppData.FileType));
        Assert.That(after.FileMetadata.Created, Is.EqualTo(before.FileMetadata.Created));
    }

    [Test]
    public async Task OneBadFileDoesNotBlockTheRest()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var current = await UploadAsync(owner, spec.TargetDrive, "current");
        var stale = await UploadAsync(owner, spec.TargetDrive, "stale");
        var deleted = await UploadAsync(owner, spec.TargetDrive, "deleted");
        var delete = await owner.Drives.Writer.SoftDeleteFile(deleted.DriveId, deleted.FileId);
        Assert.That(delete.IsSuccessStatusCode, Is.True, $"seed delete failed: {delete.StatusCode}");
        var missing = Guid.NewGuid();

        var response = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, Batch(2,
            (stale.FileId, Guid.NewGuid()),
            (missing, Guid.NewGuid()),
            (deleted.FileId, deleted.NewVersionTag),
            (current.FileId, current.NewVersionTag)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode, r.NewVersionTag.HasValue)), Is.EqualTo(new[]
        {
            (stale.FileId, (OdinClientErrorCode?)OdinClientErrorCode.VersionTagMismatch, false),
            (missing, OdinClientErrorCode.FileNotFound, false),
            (deleted.FileId, OdinClientErrorCode.CannotUpdateNonActiveFile, false),
            (current.FileId, null, true)
        }));

        Assert.That((await GetHeaderAsync(owner, current)).FileMetadata.AppData.ArchivalStatus, Is.EqualTo(2));
        var staleHeader = await GetHeaderAsync(owner, stale);
        Assert.That((staleHeader.FileMetadata.AppData.ArchivalStatus, staleHeader.FileMetadata.VersionTag),
            Is.EqualTo((0, (Guid?)stale.NewVersionTag)));
    }

    [Test]
    public async Task RestoresWithTheReturnedTagAndQueriesSeeTheColumn()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var file = await UploadAsync(owner, spec.TargetDrive, "restore me");

        var remove = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, Batch(2, (file.FileId, file.NewVersionTag)));
        Assert.That(remove.StatusCode, Is.EqualTo(HttpStatusCode.OK), remove.Error?.Content);
        Assert.That(await QueryByArchivalStatusAsync(owner, spec.TargetDrive, 2), Is.EqualTo(new[] { file.FileId }));

        var removedTag = remove.Content!.Results.Single().NewVersionTag!.Value;
        var restore = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, Batch(0, (file.FileId, removedTag)));
        Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.OK), restore.Error?.Content);
        Assert.That(restore.Content!.Results.Single().ErrorCode, Is.Null);

        Assert.That(await QueryByArchivalStatusAsync(owner, spec.TargetDrive, 2), Is.Empty);
        Assert.That(await QueryByArchivalStatusAsync(owner, spec.TargetDrive, 0), Is.EqualTo(new[] { file.FileId }));
    }

    [Test]
    public async Task RejectsMalformedBatches()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, _) = await SetupCallerWithOwner(spec);
        var duplicate = Guid.NewGuid();
        var batches = new Dictionary<string, UpdateArchivalStatusBatchRequestV2>
        {
            ["empty"] = Batch(2),
            ["too many"] = Batch(2, Enumerable.Range(0, UpdateArchivalStatusBatchRequestV2.MaxFiles + 1)
                .Select(_ => (Guid.NewGuid(), Guid.NewGuid())).ToArray()),
            ["duplicate"] = Batch(2, (duplicate, Guid.NewGuid()), (duplicate, Guid.NewGuid())),
            ["no version tag"] = Batch(2, (Guid.NewGuid(), Guid.Empty)),
        };

        var statuses = new Dictionary<string, HttpStatusCode>();
        foreach (var (name, batch) in batches)
        {
            statuses[name] = (await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, batch)).StatusCode;
        }

        Assert.That(statuses, Is.EqualTo(batches.Keys.ToDictionary(k => k, _ => HttpStatusCode.BadRequest)));
    }

    private static UpdateArchivalStatusBatchRequestV2 Batch(int status, params (Guid FileId, Guid VersionTag)[] files) => new()
    {
        ArchivalStatus = status,
        Files = files.Select(f => new UpdateArchivalStatusFileV2 { FileId = f.FileId, VersionTag = f.VersionTag }).ToList()
    };

    private static async Task<CreateFileResult> UploadAsync(OwnerSession owner, TargetDrive drive, string content)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, content, acl: AccessControlList.Anonymous);
        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [] }, []);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"owner seed upload failed: {upload.StatusCode}");
        return upload.Content!;
    }

    private static async Task<SharedSecretEncryptedFileHeader> GetHeaderAsync(OwnerSession owner, CreateFileResult file)
    {
        var header = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"header read failed: {header.StatusCode}");
        return header.Content!;
    }

    private static async Task<List<Guid>> QueryByArchivalStatusAsync(OwnerSession owner, TargetDrive drive, int status)
    {
        var results = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, ArchivalStatus = [status] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = false }
        });
        Assert.That(results.IsSuccessStatusCode, Is.True, $"query failed: {results.StatusCode}");
        return results.Content!.SearchResults.Select(r => r.FileId).ToList();
    }
}
