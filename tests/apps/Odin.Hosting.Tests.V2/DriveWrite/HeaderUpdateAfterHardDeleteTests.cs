#nullable enable
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A change of a stored file is written only over its row as read: a header read, then the file hard-deleted (a purge or
/// the expiry job), then the change written, is refused as fileNotFound and the file stays gone.  Before, the write's
/// INSERT branch recreated the file from the header read before the delete, payloads whose bytes were gone included (#1963).
/// </summary>
[TestFixture]
public class HeaderUpdateAfterHardDeleteTests : V2Fixture
{
    [Test]
    public async Task AChangeWrittenAfterAHardDeleteIsRefused_AndTheFileStaysGone()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var seeded = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "to be purged");
        var file = new InternalDriveFileId(spec.TargetDrive.Alias, seeded.FileId);

        var (tenant, context) = await MigrationContextAsync(owner);
        await using var scope = tenant.BeginLifetimeScope();
        var storage = scope.Resolve<StandardFileSystem>().Storage;
        var drive = await scope.Resolve<IDriveManager>().GetDriveAsync(spec.TargetDrive.Alias, failIfInvalid: true);

        // What every change path does: read the header, change it, write it back.  The delete lands in between
        var header = await storage.GetServerFileHeader(file, context);
        await storage.HardDeleteLongTermFile(file, context);

        header.FileMetadata.AppData.Content = "changed after the delete";
        var refused = Assert.ThrowsAsync<OdinClientException>(() =>
            scope.Resolve<LongTermStorageManager>().SaveFileHeader(drive, header, useThisVersionTag: null));
        Assert.That(refused!.ErrorCode, Is.EqualTo(OdinClientErrorCode.FileNotFound), refused.Message);

        Assert.That(await storage.GetServerFileHeader(file, context), Is.Null, "the hard-deleted file is not recreated");
    }
}
