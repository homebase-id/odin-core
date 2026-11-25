using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A soft delete clears the file's unique id unless the caller asks to keep it. A kept id stays on the tombstone, where a
/// query for deleted files finds it, and stays taken until the tombstone is hard deleted.
/// </summary>
[TestFixture]
public class SoftDeleteKeepUniqueIdTests : V2Fixture
{
    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public async Task TheBatchKeepsTheUniqueIdOnlyWhenAsked(bool? keepUniqueId)
    {
        var (owner, drive, uniqueId, file) = await SeedAsync();

        var response = await owner.Drives.Writer.SoftDeleteBatch(drive.Alias, new SoftDeleteBatchRequestV2
        {
            Files = [SoftDeleteBatchTests.File(file.FileId, file.NewVersionTag)],
            Tombstone = Options(keepUniqueId)
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.Results.Single().ErrorCode, Is.Null);
        await AssertTombstoneAsync(owner, drive, uniqueId, file.FileId, keepUniqueId == true);
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public async Task TheSingleDeleteKeepsTheUniqueIdOnlyWhenAsked(bool? keepUniqueId)
    {
        var (owner, drive, uniqueId, file) = await SeedAsync();

        var response = await owner.Drives.Writer.SoftDeleteFile(drive.Alias, file.FileId,
            tombstone: Options(keepUniqueId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        await AssertTombstoneAsync(owner, drive, uniqueId, file.FileId, keepUniqueId == true);
    }

    [Test]
    public async Task DeletingATombstoneAgainKeepsAnIdItKept()
    {
        var (owner, drive, uniqueId, file) = await SeedAsync();
        var keep = await owner.Drives.Writer.SoftDeleteFile(drive.Alias, file.FileId, tombstone: Options(true));
        Assert.That(keep.StatusCode, Is.EqualTo(HttpStatusCode.OK), keep.Error?.Content);

        // e.g. a delete for me, then for everyone, which names no options
        var again = await owner.Drives.Writer.SoftDeleteFile(drive.Alias, file.FileId);
        var batch = await owner.Drives.Writer.SoftDeleteBatch(drive.Alias,
            new SoftDeleteBatchRequestV2 { Files = [SoftDeleteBatchTests.File(file.FileId)] });

        Assert.That((again.StatusCode, batch.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)),
            again.Error?.Content ?? batch.Error?.Content);
        await AssertTombstoneAsync(owner, drive, uniqueId, file.FileId, kept: true);
    }

    [Test]
    public async Task AHardDeleteFreesAKeptId()
    {
        var (owner, drive, uniqueId, file) = await SeedAsync();
        var keep = await owner.Drives.Writer.SoftDeleteFile(drive.Alias, file.FileId, tombstone: Options(true));
        Assert.That(keep.StatusCode, Is.EqualTo(HttpStatusCode.OK), keep.Error?.Content);
        var tombstone = await AppFileUploads.HeaderAsync(owner, drive, file.FileId);

        var hardDelete = await owner.Drives.Writer.HardDeleteBatch(drive.Alias, new HardDeleteBatchRequestV2
        {
            Files = [new FileVersionTagV2 { FileId = file.FileId, VersionTag = tombstone.FileMetadata.VersionTag }]
        });

        Assert.That(hardDelete.StatusCode, Is.EqualTo(HttpStatusCode.OK), hardDelete.Error?.Content);
        Assert.That(hardDelete.Content!.Results.Single().ErrorCode, Is.Null);
        await AssertTombstoneAsync(owner, drive, uniqueId, file.FileId, kept: false);
    }

    /// <summary>Null leaves the tombstone options out of the request</summary>
    private static TombstoneOptions Options(bool? keepUniqueId) =>
        keepUniqueId.HasValue ? new TombstoneOptions { KeepUniqueId = keepUniqueId.Value } : null;

    /// <summary>
    /// Whether a query for deleted files finds the tombstone by its unique id, and whether a new file may take the id
    /// </summary>
    private static async Task AssertTombstoneAsync(OwnerSession owner, TargetDrive drive, Guid uniqueId, Guid fileId,
        bool kept)
    {
        var byUniqueId = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive, ClientUniqueIdAtLeastOne = [uniqueId], FileState = [FileState.Active, FileState.Deleted]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        });
        Assert.That(byUniqueId.StatusCode, Is.EqualTo(HttpStatusCode.OK), byUniqueId.Error?.Content);
        var found = byUniqueId.Content!.SearchResults.Select(f => (f.FileId, f.FileState, f.FileMetadata.AppData.UniqueId)).ToList();
        Assert.That(found, Is.EqualTo(kept ? new[] { (fileId, FileState.Deleted, (Guid?)uniqueId) } : []));

        var again = await AppFileUploads.TryCreateTextFileAsync(owner, drive, "again", uniqueId: uniqueId);
        if (kept)
        {
            DriveAsserts.AssertRejected(again, OdinClientErrorCode.ExistingFileWithUniqueId);
        }
        else
        {
            Assert.That(again.IsSuccessStatusCode, Is.True, $"actual {again.StatusCode}: {again.Error?.Content}");
        }
    }

    private async Task<(OwnerSession Owner, TargetDrive Drive, Guid UniqueId, CreateFileResult File)> SeedAsync()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var uniqueId = Guid.NewGuid();
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "kept or not", uniqueId: uniqueId);
        return (owner, spec.TargetDrive, uniqueId, file);
    }
}
