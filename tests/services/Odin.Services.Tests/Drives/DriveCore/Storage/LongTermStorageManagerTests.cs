using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

// Covers the long-term primitives the folder-less inbox path relies on:
//  - WriteStreamDirectToLongTermAsync: the peer receive path streams a payload straight to long-term under the
//    incoming fileId, at exactly the location the canonical reader (PayloadExistsOnDiskAsync) later reads from.
//  - TryHardDeleteListOfPayloadFiles: what CleanupAbandonedLongTermPayloads uses to reclaim those payloads when
//    an inbox item is abandoned / fails to enqueue (otherwise they orphan, since the orphan scanner only sweeps
//    the inbox folder).
//  - MovePayloadWithinLongTermAsync: the overwrite path relocates payloads (and thumbnails) from the incoming
//    fileId to the resolved target fileId.
// These are permanent (new-design) behaviors, not part of the dual-read transition.
public class LongTermStorageManagerTests : PayloadReaderWriterBaseTestFixture
{
    private const string Key = "testkey01";

    private TenantContext _tenantContext = null!;
    private TenantPathManager _tenantPathManager = null!;
    private LongTermStorageManager _sut = null!;

    [SetUp]
    public void Setup()
    {
        BaseSetup();

        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
                FileWriteChunkSizeInBytes = 4096,
            }
        };

        var tenantId = Guid.NewGuid();
        _tenantContext = new TenantContext(
            tenantId,
            new OdinId("frodo.me"),
            new TenantPathManager(config, tenantId),
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null,
            email: null);
        _tenantPathManager = _tenantContext.TenantPathManager;

        var fileReaderWriter = new FileReaderWriter(config, new Mock<ILogger<FileReaderWriter>>().Object);
        var longTermPayloadStore = new LongTermPayloadStore(new DiskFileStore(fileReaderWriter));

        // Only logger, the payload store, and the tenant context (for TenantPathManager) are exercised by the
        // methods under test; the DB-facing dependencies are never touched, so null is safe here.
        _sut = new LongTermStorageManager(
            new Mock<ILogger<LongTermStorageManager>>().Object,
            longTermPayloadStore,
            driveQuery: null!,
            scopedIdentityTransactionFactory: null!,
            tableDriveTransferHistory: null!,
            driveMainIndex: null!,
            _tenantContext,
            forgottenTasks: null!);
    }

    [TearDown]
    public void TearDown() => BaseTearDown();

    //

    [Test]
    public async Task WriteStreamDirectToLongTerm_LandsWhereCanonicalReaderLooks_AndHardDeleteRemovesIt()
    {
        var drive = NewDrive();
        var fileId = Guid.NewGuid();
        var uid = UnixTimeUtcUnique.ZeroTime;
        var bytes = Encoding.UTF8.GetBytes("payload-bytes");
        var extension = TenantPathManager.GetBasePayloadFileNameAndExtension(Key, uid);

        await using (var ms = new MemoryStream(bytes))
        {
            var written = await _sut.WriteStreamDirectToLongTermAsync(drive, fileId, extension, ms);
            Assert.That(written, Is.EqualTo((uint)bytes.Length));
        }

        var descriptor = new PayloadDescriptor { Key = Key, Uid = uid };

        // The directly-written payload is visible to the canonical reader (no copy needed at commit time).
        Assert.That(await _sut.PayloadExistsOnDiskAsync(drive, fileId, descriptor), Is.True);

        // CleanupAbandonedLongTermPayloads reclaims it via this hard delete on the give-up path.
        await _sut.TryHardDeleteListOfPayloadFiles(drive, fileId, new List<PayloadDescriptor> { descriptor });
        Assert.That(await _sut.PayloadExistsOnDiskAsync(drive, fileId, descriptor), Is.False);
    }

    //

    [Test]
    public async Task MovePayloadWithinLongTerm_RelocatesPayloadAndThumbnail_FromIncomingToTargetFileId()
    {
        var drive = NewDrive();
        var incomingFileId = Guid.NewGuid();
        var targetFileId = Guid.NewGuid();
        var uid = UnixTimeUtcUnique.ZeroTime;
        var thumb = new ThumbnailDescriptor { PixelWidth = 10, PixelHeight = 20, BytesWritten = 5, ContentType = "image/png" };
        var descriptor = new PayloadDescriptor { Key = Key, Uid = uid, Thumbnails = new List<ThumbnailDescriptor> { thumb } };

        await WriteAsync(drive, incomingFileId, TenantPathManager.GetBasePayloadFileNameAndExtension(Key, uid), "payload");
        await WriteAsync(drive, incomingFileId,
            TenantPathManager.GetThumbnailFileNameAndExtension(Key, uid, thumb.PixelWidth, thumb.PixelHeight), "thumb");

        // Sanity: both present under the incoming fileId.
        Assert.That(await _sut.PayloadExistsOnDiskAsync(drive, incomingFileId, descriptor), Is.True);
        Assert.That(await _sut.ThumbnailExistsOnDiskAsync(drive, incomingFileId, descriptor, thumb), Is.True);

        await _sut.MovePayloadWithinLongTermAsync(drive, incomingFileId, targetFileId, descriptor);

        // Gone from the incoming fileId, present under the target fileId.
        Assert.That(await _sut.PayloadExistsOnDiskAsync(drive, incomingFileId, descriptor), Is.False);
        Assert.That(await _sut.ThumbnailExistsOnDiskAsync(drive, incomingFileId, descriptor, thumb), Is.False);
        Assert.That(await _sut.PayloadExistsOnDiskAsync(drive, targetFileId, descriptor), Is.True);
        Assert.That(await _sut.ThumbnailExistsOnDiskAsync(drive, targetFileId, descriptor, thumb), Is.True);
    }

    //

    private StorageDrive NewDrive() =>
        new(_tenantPathManager, new StorageDriveData { Id = Guid.NewGuid(), TargetDriveInfo = TargetDrive.NewTargetDrive() });

    private async Task WriteAsync(StorageDrive drive, Guid fileId, string extension, string content)
    {
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _sut.WriteStreamDirectToLongTermAsync(drive, fileId, extension, ms);
    }
}
