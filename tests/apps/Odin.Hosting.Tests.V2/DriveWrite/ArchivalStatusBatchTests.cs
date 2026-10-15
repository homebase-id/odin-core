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

        var response = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, new UpdateArchivalStatusBatchRequestV2
        {
            ArchivalStatus = 2,
            Files =
            [
                new UpdateArchivalStatusFileV2 { FileId = a.FileId, VersionTag = a.NewVersionTag },
                new UpdateArchivalStatusFileV2 { FileId = b.FileId, VersionTag = b.NewVersionTag }
            ]
        });

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

        var response = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, new UpdateArchivalStatusBatchRequestV2
        {
            ArchivalStatus = 2,
            Files =
            [
                new UpdateArchivalStatusFileV2 { FileId = stale.FileId, VersionTag = Guid.NewGuid() },
                new UpdateArchivalStatusFileV2 { FileId = missing, VersionTag = Guid.NewGuid() },
                new UpdateArchivalStatusFileV2 { FileId = deleted.FileId, VersionTag = deleted.NewVersionTag },
                new UpdateArchivalStatusFileV2 { FileId = current.FileId, VersionTag = current.NewVersionTag }
            ]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Select(r => (r.FileId, r.ErrorCode)), Is.EqualTo(new[]
        {
            (stale.FileId, (OdinClientErrorCode?)OdinClientErrorCode.VersionTagMismatch),
            (missing, OdinClientErrorCode.FileNotFound),
            (deleted.FileId, OdinClientErrorCode.CannotUpdateNonActiveFile),
            (current.FileId, null)
        }));
        Assert.That(response.Content.Results.Select(r => r.NewVersionTag.HasValue), Is.EqualTo(new[] { false, false, false, true }));

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

        var remove = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, Single(file.FileId, file.NewVersionTag, 2));
        Assert.That(remove.StatusCode, Is.EqualTo(HttpStatusCode.OK), remove.Error?.Content);
        Assert.That(await QueryByArchivalStatusAsync(owner, spec.TargetDrive, 2), Is.EqualTo(new[] { file.FileId }));

        var removedTag = remove.Content!.Results.Single().NewVersionTag!.Value;
        var restore = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, Single(file.FileId, removedTag, 0));
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
        var batches = new Dictionary<string, List<UpdateArchivalStatusFileV2>>
        {
            ["empty"] = [],
            ["too many"] = Enumerable.Range(0, UpdateArchivalStatusBatchRequestV2.MaxFiles + 1)
                .Select(_ => new UpdateArchivalStatusFileV2 { FileId = Guid.NewGuid(), VersionTag = Guid.NewGuid() }).ToList(),
            ["duplicate"] =
            [
                new UpdateArchivalStatusFileV2 { FileId = duplicate, VersionTag = Guid.NewGuid() },
                new UpdateArchivalStatusFileV2 { FileId = duplicate, VersionTag = Guid.NewGuid() }
            ],
            ["no version tag"] = [new UpdateArchivalStatusFileV2 { FileId = Guid.NewGuid() }],
        };

        var statuses = new Dictionary<string, HttpStatusCode>();
        foreach (var (name, files) in batches)
        {
            var response = await caller.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias,
                new UpdateArchivalStatusBatchRequestV2 { ArchivalStatus = 2, Files = files });
            statuses[name] = response.StatusCode;
        }

        Assert.That(statuses, Is.EqualTo(batches.Keys.ToDictionary(k => k, _ => HttpStatusCode.BadRequest)));
    }

    private static UpdateArchivalStatusBatchRequestV2 Single(Guid fileId, Guid versionTag, int status) => new()
    {
        ArchivalStatus = status,
        Files = [new UpdateArchivalStatusFileV2 { FileId = fileId, VersionTag = versionTag }]
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
