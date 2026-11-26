#nullable enable
using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A new file whose global transit id another file on the drive has is refused at the commit by the database's unique
/// index, and answered ExistingFileWithGlobalTransitId, as the check before the body is (#2085).  Before, the index's
/// clash answered ExistingFileWithUniqueId, so the upload checked the id again by hand at finalize.
/// </summary>
[TestFixture]
public class GlobalTransitIdClashTests : V2Fixture
{
    [Test]
    public async Task ANewFileHeaderWithATakenGlobalTransitIdIsAnsweredAsAGlobalTransitIdClash()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var first = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "first");
        var second = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "second");
        var takenGtid = first.GlobalTransitId!.Value;

        var (tenant, context) = await MigrationContextAsync(owner);
        await using var scope = tenant.BeginLifetimeScope();
        var drive = await scope.Resolve<IDriveManager>().GetDriveAsync(spec.TargetDrive.Alias, failIfInvalid: true);
        var header = await scope.Resolve<StandardFileSystem>().Storage.GetServerFileHeader(
            new InternalDriveFileId(spec.TargetDrive.Alias, second.FileId), context);

        // The second file's header as a new file with the first one's global transit id, written in a transaction as a
        // commit is: what the upload of a file that took the id while this one's body arrived writes
        header.FileMetadata.File = header.FileMetadata.File with { FileId = Guid.NewGuid() };
        header.FileMetadata.GlobalTransitId = takenGtid;
        header.FileMetadata.VersionTag = null;

        await using var tx = await scope.Resolve<IdentityDatabase>().BeginStackedTransactionAsync();
        var refused = Assert.ThrowsAsync<OdinClientException>(() => scope.Resolve<DriveQuery>().SaveFileHeaderAsync(drive, header, null, newFile: true));
        Assert.That(refused!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ExistingFileWithGlobalTransitId), refused.Message);
    }
}
