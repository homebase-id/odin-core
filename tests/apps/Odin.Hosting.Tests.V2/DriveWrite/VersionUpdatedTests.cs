#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A file's <c>versionUpdated</c>: the <c>updated</c> of the write that gave it its version tag. Every write that gives a
/// new tag sets it, in the header, the minimal shape and the write's answer alike; a write that keeps the tag moves
/// <c>updated</c> alone. V2 only.
/// </summary>
[TestFixture]
public class VersionUpdatedTests : V2Fixture
{
    [Test]
    public async Task EveryWriteThatGivesANewVersionTagSetsItAndOthersLeaveIt()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var drive = spec.TargetDrive;

        var created = await AppFileUploads.SeedTextFileAsync(owner, drive, "first");
        var afterCreate = await AssertVersionUpdatedIsUpdatedAsync(owner, drive, created.FileId, "a new file");
        Assert.That(created.VersionUpdated, Is.EqualTo(afterCreate), "the create's answer");

        // Local app data keeps the tag: updated moves, versionUpdated stays
        var tags = await owner.Drives.Writer.UpdateLocalAppMetadataTags(drive.Alias, created.FileId,
            new UpdateLocalMetadataTagsRequestV2 { LocalVersionTag = Guid.Empty, Tags = [Guid.NewGuid()] });
        Assert.That(tags.StatusCode, Is.EqualTo(HttpStatusCode.OK), tags.Error?.Content);
        var touched = await AppFileUploads.HeaderAsync(owner, drive, created.FileId);
        Assert.That(touched.FileMetadata.VersionUpdated, Is.EqualTo((UnixTimeUtc?)afterCreate), "after local tags, versionUpdated");
        Assert.That(touched.FileMetadata.Updated.milliseconds, Is.GreaterThan(afterCreate.milliseconds), "after local tags, updated moved");
        Assert.That((await AppFileUploads.FeedAsync(owner, drive))[created.FileId].VersionUpdated, Is.EqualTo(afterCreate),
            "after local tags, the minimal shape");

        // An update gives a new tag
        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "second", acl: AccessControlList.Anonymous);
        metadata.VersionTag = touched.FileMetadata.VersionTag;
        var update = await owner.Drives.Writer.UpdateFileByFileId(drive.Alias, created.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        var afterUpdate = await AssertVersionUpdatedIsUpdatedAsync(owner, drive, created.FileId, "an update");
        Assert.That(update.Content!.VersionUpdated, Is.EqualTo((UnixTimeUtc?)afterUpdate), "the update's answer");
        Assert.That(afterUpdate.milliseconds, Is.GreaterThan(touched.FileMetadata.Updated.milliseconds), "the update, past the touch");

        // Archival status gives a new tag
        var archive = await owner.Drives.Writer.UpdateArchivalStatusBatch(drive.Alias, new UpdateArchivalStatusBatchRequestV2
        {
            ArchivalStatus = 2,
            Files = [new FileVersionTagV2 { FileId = created.FileId, VersionTag = update.Content.NewVersionTag }]
        });
        Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.OK), archive.Error?.Content);
        var afterArchive = await AssertVersionUpdatedIsUpdatedAsync(owner, drive, created.FileId, "archival status");
        var archived = archive.Content!.Results.Single();
        Assert.That((archived.ErrorCode, archived.VersionUpdated), Is.EqualTo(((Odin.Core.Exceptions.OdinClientErrorCode?)null, (UnixTimeUtc?)afterArchive)),
            "the archival batch's row");

        // A soft delete gives the tombstone a new tag: the delete's time, below its final updated (clearing the reaction
        // summary moves that once more)
        var delete = await owner.Drives.Writer.SoftDeleteBatch(drive.Alias,
            new SoftDeleteBatchRequestV2 { Files = [new SoftDeleteFileV2 { FileId = created.FileId }] });
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        var deleted = delete.Content!.Results.Single();
        var tombstone = await AppFileUploads.HeaderAsync(owner, drive, created.FileId);
        Assert.That((deleted.ErrorCode, deleted.VersionUpdated), Is.EqualTo(((Odin.Core.Exceptions.OdinClientErrorCode?)null,
            tombstone.FileMetadata.VersionUpdated)), "the soft delete batch's row, as the tombstone has it");
        Assert.That(tombstone.FileMetadata.VersionUpdated!.Value.milliseconds,
            Is.GreaterThan(afterArchive.milliseconds).And.LessThanOrEqualTo(tombstone.FileMetadata.Updated.milliseconds),
            "the tombstone's versionUpdated: after the archive, not past its updated");
        Assert.That((await AppFileUploads.FeedAsync(owner, drive))[created.FileId].VersionUpdated,
            Is.EqualTo(tombstone.FileMetadata.VersionUpdated), "the tombstone in the minimal shape");
    }

    [Test]
    public async Task ASingleSoftDeleteAnswersTheTombstonesVersionUpdated()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "to delete");

        var delete = await owner.Drives.Writer.SoftDeleteFile(spec.TargetDrive.Alias, file.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        var tombstone = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, file.FileId);
        Assert.That(tombstone.FileMetadata.VersionUpdated, Is.Not.Null, "the tombstone's versionUpdated");
        Assert.That(delete.Content!.VersionUpdated, Is.EqualTo(tombstone.FileMetadata.VersionUpdated), "the delete's answer");
    }

    [Test]
    public async Task AnAnonymousV2ReadIsAnsweredVersionUpdated()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "public");

        var anonymous = Host.AnonymousRefitFor<Odin.Hosting.Tests._V2.ApiClient.IDriveReaderHttpClientApiV2>(owner.Identity.DomainName);
        var read = await anonymous.GetFileHeader(spec.TargetDrive.Alias, file.FileId, FileSystemType.Standard);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        Assert.That(read.Content!.FileMetadata.VersionUpdated, Is.EqualTo(file.VersionUpdated), "with no token, as with one");
    }

    [Test]
    public async Task V1AnswersNoVersionUpdated()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "read by V1");

        var v1 = await owner.V1.Drive.GetFileHeader(new ExternalFileIdentifier { FileId = file.FileId, TargetDrive = spec.TargetDrive });
        Assert.That(v1.StatusCode, Is.EqualTo(HttpStatusCode.OK), v1.Error?.Content);
        Assert.That((v1.Content!.FileMetadata.VersionUpdated, (await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, file.FileId)).FileMetadata.VersionUpdated),
            Is.EqualTo(((UnixTimeUtc?)null, file.VersionUpdated)), "(V1's header, V2's header)");
    }

    /// <summary>The file's header; its versionUpdated is its updated, as when the last write gave it its tag</summary>
    private static async Task<UnixTimeUtc> AssertVersionUpdatedIsUpdatedAsync(OwnerSession owner, TargetDrive drive, Guid fileId,
        string after)
    {
        var header = await AppFileUploads.HeaderAsync(owner, drive, fileId);
        var minimal = (await AppFileUploads.FeedAsync(owner, drive))[fileId];
        Assert.That((header.FileMetadata.VersionUpdated, minimal.VersionUpdated, minimal.Updated),
            Is.EqualTo(((UnixTimeUtc?)header.FileMetadata.Updated, (UnixTimeUtc?)header.FileMetadata.Updated, header.FileMetadata.Updated)),
            $"after {after}: (header versionUpdated, minimal versionUpdated, minimal updated) all the header's updated");
        return header.FileMetadata.Updated;
    }
}
